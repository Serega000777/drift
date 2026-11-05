using DriftCareer.Core.Surfaces;
using UnityEngine;

namespace DriftCareer.Runtime.Track
{
    /// <summary>Marks a collider (or its children) with a surface profile id used by the tire model.</summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceTag : MonoBehaviour
    {
        public string surfaceId = SurfaceLibrary.DryAsphalt;
    }
}
