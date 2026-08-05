using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Pulls individual named objects out of the multi-object Blender FBX files so the
    /// environment builder can place them.
    ///
    /// The exports pack many props into one file (Props.fbx, Foliage.fbx, Landmarks.fbx), and
    /// each object's root carries the Blender-to-Unity axis conversion. This flattens a named
    /// root into a list of (mesh, matrix) pairs already in that object's own upright space, which
    /// is exactly what the mesh combiner wants.
    /// </summary>
    public static class DuckAssetLibrary
    {
        const string ModelDir = "Assets/Art/Models";

        public struct Piece
        {
            public Mesh mesh;
            public Matrix4x4 localToRoot;
        }

        static readonly Dictionary<string, GameObject> _cache = new();

        static GameObject LoadFile(string file)
        {
            if (_cache.TryGetValue(file, out var go) && go != null) return go;
            go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{file}");
            _cache[file] = go;
            return go;
        }

        public static void ClearCache() => _cache.Clear();

        /// <summary>
        /// Every mesh under the named root, with matrices relative to that root — including the
        /// root's own axis-conversion rotation, so the result stands upright at the origin.
        /// Returns an empty list when the object is not present, which lets callers fall back to
        /// the procedural stand-in without a special case.
        /// </summary>
        public static List<Piece> GetPieces(string file, string rootName, string excludeChild = null)
        {
            var result = new List<Piece>();
            var asset = LoadFile(file);
            if (asset == null) return result;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                var root = DuckModelIntegration.FindDeep(inst.transform, rootName);
                if (root == null) return result;

                // Express each mesh relative to the root's PARENT rather than the root itself.
                // That keeps the root's axis-conversion rotation in the matrix, so the pieces come
                // out Y-up and facing +Z; subtracting the root's translation puts the object on
                // the origin so it can be placed anywhere.
                Matrix4x4 worldToRootParent = root.parent != null
                    ? root.parent.worldToLocalMatrix
                    : Matrix4x4.identity;
                Matrix4x4 recentre = Matrix4x4.Translate(-root.localPosition);

                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    if (excludeChild != null && IsUnder(mf.transform, excludeChild)) continue;
                    Matrix4x4 rel = recentre * worldToRootParent * mf.transform.localToWorldMatrix;
                    result.Add(new Piece { mesh = mf.sharedMesh, localToRoot = rel });
                }
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
            return result;
        }

        static bool IsUnder(Transform t, string ancestorName)
        {
            for (var c = t; c != null; c = c.parent)
                if (c.name == ancestorName) return true;
            return false;
        }

        /// <summary>Single mesh for a root that has exactly one, else null.</summary>
        public static Mesh GetSingleMesh(string file, string rootName)
        {
            var pieces = GetPieces(file, rootName);
            return pieces.Count == 1 ? pieces[0].mesh : null;
        }

        /// <summary>
        /// A combined mesh for a whole named object, baked into its own upright space and saved
        /// as an asset. Used for props placed many times, so each is one draw rather than several.
        /// </summary>
        public static Mesh GetCombined(string file, string rootName, string saveName, string excludeChild = null)
        {
            string path = $"Assets/Meshes/Authored/{saveName}.asset";
            var cached = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (cached != null) return cached;

            var pieces = GetPieces(file, rootName, excludeChild);
            if (pieces.Count == 0) return null;

            var combines = new CombineInstance[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
                combines[i] = new CombineInstance { mesh = pieces[i].mesh, transform = pieces[i].localToRoot, subMeshIndex = 0 };

            var mesh = new Mesh { name = saveName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(combines, true, true);
            // CombineMeshes clears the name it was given; set it back so it matches the filename.
            mesh.name = saveName;
            mesh.RecalculateBounds();

            EnsureFolder("Assets/Meshes/Authored");
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// Where a child object sits relative to another root, in the same space
        /// <see cref="GetCombined"/> bakes into — Y-up, facing +Z, origin on the root.
        ///
        /// GetCombined deliberately recentres every object on its own pivot so it can be placed
        /// anywhere, which means an assembly's internal layout is lost the moment you combine the
        /// parts separately. Anything that needs to reassemble those parts — a windmill's sails
        /// onto its tower, say — has to ask for the offset rather than guess it. Guessing is how
        /// the sails ended up turning inside the tower instead of on the front of it.
        /// </summary>
        public static bool TryGetLocalOffset(string file, string rootName, string childName, out Vector3 offset)
        {
            offset = Vector3.zero;
            var asset = LoadFile(file);
            if (asset == null) return false;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                var root = DuckModelIntegration.FindDeep(inst.transform, rootName);
                var child = DuckModelIntegration.FindDeep(inst.transform, childName);
                if (root == null || child == null) return false;

                Matrix4x4 worldToRootParent = root.parent != null
                    ? root.parent.worldToLocalMatrix
                    : Matrix4x4.identity;
                Vector3 rootInParent = worldToRootParent.MultiplyPoint3x4(root.position);
                Vector3 childInParent = worldToRootParent.MultiplyPoint3x4(child.position);
                offset = childInParent - rootInParent;
                return true;
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        [MenuItem("Duck/Diagnose · List authored objects", priority = 49)]
        public static void ListObjects()
        {
            var sb = new System.Text.StringBuilder("[Duck] AUTHORED OBJECTS\n");
            // Every model file, not a hand-kept subset. The list going stale is how a missing
            // portrait turns into a guessing game about node names.
            var files = new System.Collections.Generic.List<string>();
            foreach (string full in System.IO.Directory.GetFiles(ModelDir, "*.fbx", System.IO.SearchOption.AllDirectories))
                files.Add(System.IO.Path.GetFileName(full));
            files.Sort();
            foreach (var f in files)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{f}");
                if (asset == null) { sb.AppendLine($"  {f}: MISSING"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                var names = new List<string>();
                for (int i = 0; i < inst.transform.childCount; i++)
                    names.Add(inst.transform.GetChild(i).name);
                sb.AppendLine($"  {f}: {string.Join(", ", names)}");
                Object.DestroyImmediate(inst);
            }
            Debug.Log(sb.ToString());
        }
    }
}
