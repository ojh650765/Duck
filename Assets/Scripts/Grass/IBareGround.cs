using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Somewhere the grass does not grow.
    ///
    /// <see cref="GrassField"/> bakes two blade meshes and instances them across every chunk, which
    /// is what keeps a lawn of hundreds of thousands of blades affordable in a browser — and it also
    /// means the field has no idea what is standing on it. That is fine for the mowing round, where
    /// the field IS the level, and wrong for the goose arena, where a third of the ground is bare
    /// earth and flowerbeds with grass growing straight through them.
    ///
    /// An interface rather than a list of rectangles, because the arena's strips and gardens sit on
    /// diagonals: only the thing that owns the layout can answer the question, and
    /// <see cref="RallyArena"/> already does.
    /// </summary>
    public interface IBareGround
    {
        /// <summary>True where no blade should root, in world space.</summary>
        bool IsBare(Vector3 world);
    }
}
