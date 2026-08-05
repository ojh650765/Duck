using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The single source of truth for playfield dimensions and the mappings between
    /// world space, cut-mask UV space and the CPU scoring grid.
    /// Everything that touches the lawn goes through here.
    /// </summary>
    public static class Field
    {
        /// <summary>Playfield is a square centred on the origin, x,z in [-Half, +Half].</summary>
        public const float Size = 64f;
        public const float Half = Size * 0.5f;

        /// <summary>GPU cut mask resolution. 16 px/m at 64 m.</summary>
        public const int MaskRes = 1024;

        /// <summary>CPU scoring grid resolution. 25 cm/cell at 64 m.</summary>
        public const int GridRes = 256;

        public const float MetresPerCell = Size / GridRes;
        public const float CellArea = MetresPerCell * MetresPerCell;

        /// <summary>World XZ -> [0,1] mask UV.</summary>
        public static Vector2 WorldToUV(Vector3 world)
            => new Vector2((world.x + Half) / Size, (world.z + Half) / Size);

        /// <summary>World XZ -> continuous grid coordinates (may be out of range).</summary>
        public static Vector2 WorldToGrid(Vector3 world)
            => new Vector2((world.x + Half) / Size * GridRes, (world.z + Half) / Size * GridRes);

        /// <summary>Grid cell centre -> world position on the lawn.</summary>
        public static Vector3 GridToWorld(int gx, int gz)
            => new Vector3((gx + 0.5f) / GridRes * Size - Half, 0f, (gz + 0.5f) / GridRes * Size - Half);

        /// <summary>Normalised [-1,1] shape space -> world. Target shapes are authored in this space.</summary>
        public static Vector3 ShapeToWorld(Vector2 s) => new Vector3(s.x * Half, 0f, s.y * Half);

        public static bool InsidePlayfield(Vector3 world, float margin = 0f)
            => Mathf.Abs(world.x) <= Half - margin && Mathf.Abs(world.z) <= Half - margin;

        /// <summary>Clamp a world position to the lawn.</summary>
        public static Vector3 ClampToPlayfield(Vector3 world, float margin = 0f)
        {
            world.x = Mathf.Clamp(world.x, -Half + margin, Half - margin);
            world.z = Mathf.Clamp(world.z, -Half + margin, Half - margin);
            return world;
        }

        // ---- Shader global IDs, set once by CutMask and read by every lawn shader. ----
        public static readonly int IdCutMask = Shader.PropertyToID("_CutMask");
        public static readonly int IdFieldSize = Shader.PropertyToID("_FieldSize");
        public static readonly int IdFieldHalf = Shader.PropertyToID("_FieldHalf");
    }
}
