using System;
using System.Collections.Generic;
using UnityEngine.Experimental.XR;
using UnityEngine.XR.ARExtensions;

namespace UnityEngine.XR.ARFoundation
{
    /// <summary>
    /// Represents a plane detected by an AR device.
    /// </summary>
    /// <remarks>
    /// Generated by the <see cref="ARPlaneManager"/> when an AR device detects
    /// a plane in the environment.
    /// </remarks>
    [DisallowMultipleComponent]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@1.0/api/UnityEngine.XR.ARFoundation.ARPlane.html")]
    public sealed class ARPlane : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("If true, this component's GameObject will be removed immediately when the plane is removed.")]
        bool m_DestroyOnRemoval = true;

        /// <summary>
        /// If true, this component's <c>GameObject</c> will be removed immediately when the plane is removed.
        /// </summary>
        /// <remarks>
        /// Setting this to false will keep the plane's <c>GameObject</c> around. You may want to do this, for example,
        /// if you have custom removal logic, such as a fade out.
        /// </remarks>
        public bool destroyOnRemoval
        {
            get { return m_DestroyOnRemoval; }
            set { m_DestroyOnRemoval = value; }
        }

        [SerializeField]
        [Tooltip("The largest value by which a plane's vertex may change before the polygonChanged event is invoked. Units are in session-space meters.")]
        float m_VertexChangedThreshold = 0.01f;

        /// <summary>
        /// The largest value by which a plane's vertex may change before the mesh is regenerated. Units are in session-space meters.
        /// </summary>
        public float vertexChangedThreshold
        {
            get { return m_VertexChangedThreshold; }
            set { m_VertexChangedThreshold = value; }
        }

        /// <summary>
        /// The <c>BoundedPlane</c> data struct which defines this <see cref="ARPlane"/>.
        /// </summary>
        public BoundedPlane boundedPlane
        {
            get { return m_BoundedPlane; }

            internal set
            {
                m_BoundedPlane = value;

                lastUpdatedFrame = Time.frameCount;

                var pose = boundedPlane.Pose;
                transform.localPosition = pose.position;
                transform.localRotation = pose.rotation;
                m_TrackingState = null;

                if (updated != null)
                    updated(this);

                if (boundaryChanged != null)
                    CheckBoundaryChanged();
            }
        }

        /// <summary>
        /// Gets the current <c>TrackingState</c> of this <see cref="ARPlane"/>.
        /// </summary>
        public TrackingState trackingState
        {
            get
            {
                if (!m_TrackingState.HasValue)
                {
                    // Retrieving the tracking state can be expensive,
                    // so we get it lazily and cache the result until the next update.
                    if (ARSubsystemManager.planeSubsystem == null)
                        m_TrackingState = TrackingState.Unknown;
                    else
                        m_TrackingState = ARSubsystemManager.planeSubsystem.GetTrackingState(boundedPlane.Id);
                }

                return m_TrackingState.Value;
            }
        }

        /// <summary>
        /// The last frame on which this plane was updated.
        /// </summary>
        public int lastUpdatedFrame { get; private set; }

        /// <summary>
        /// Invoked whenever the plane updates
        /// </summary>
        public event Action<ARPlane> updated;

        /// <summary>
        /// Invoked just before the plane is about to be removed.
        /// </summary>
        public event Action<ARPlane> removed;

        /// <summary>
        /// Invoked when any vertex in the plane's polygon changes by more than <see cref="vertexChangedThreshold"/>.
        /// </summary>
        /// <remarks>
        /// The data is provided in plane-relative space. That is, relative to the
        /// <see cref="ARPlane"/>'s <c>localPosition</c> and <c>localRotation</c>.
        /// 
        /// Units are in session-space meters.
        /// </remarks>
        public event Action<ARPlaneBoundaryChangedEventArgs> boundaryChanged;

        /// <summary>
        /// Attempts to retrieve the boundary points of the plane.
        /// </summary>
        /// <param name="boundaryOut">If successful, the contents are replaced with the <see cref="ARPlane"/>'s boundary points.</param>
        /// <param name="space">Which coordinate system to use. <c>Space.Self</c> refers to session space,
        /// while <c>Space.World</c> refers to Unity world space. The default is <c>Space.World</c>.</param>
        /// <returns>True if the boundary was successfully retrieved.</returns>
        public bool TryGetBoundary(List<Vector3> boundaryOut, Space space = Space.World)
        {
            if (!boundedPlane.TryGetBoundary(boundaryOut))
                return false;

            if (space == Space.World)
                transform.parent.TransformPointList(boundaryOut);

            return true;
        }

        /// <summary>
        /// Checks if each set of positions are equal to within some tolerance.
        /// </summary>
        /// <param name="positions1">The first set of positions</param>
        /// <param name="positions2">The second set of positions</param>
        /// <param name="tolerance">Positions in the first set must be within this value to the positions in the second set to be considered equal.</param>
        /// <returns>True if all positions are approximately equal, false if any of them are not, or if the number of positions in the sets differs.</returns>
        public static bool PositionsAreApproximatelyEqual(List<Vector3> positions1, List<Vector3> positions2, float tolerance)
        {
            if (positions1 == null)
                throw new ArgumentNullException("positions1");

            if (positions2 == null)
                throw new ArgumentNullException("positions2");

            if (positions1.Count != positions2.Count)
                return false;

            var toleranceSquared = tolerance * tolerance;
            for (int i = 0; i < positions1.Count; ++i)
            {
                var diffSquared = (positions1[i] - positions2[i]).sqrMagnitude;
                if (diffSquared > toleranceSquared)
                    return false;
            }

            return true;
        }

        void CheckBoundaryChanged()
        {
            var newVertices = ARDataCache.vector3List;
            if (!boundedPlane.TryGetBoundary(newVertices))
                return;

            // Only perform the boundary check if our changed threshold is non zero
            if (vertexChangedThreshold > 0f)
            {
                if (m_Boundary == null)
                    m_Boundary = new List<Vector3>();

                // Early out if the verts haven't changed significantly.
                if (PositionsAreApproximatelyEqual(newVertices, m_Boundary, vertexChangedThreshold))
                    return;

                // Save the new vertices so we can compare during the next update
                ARDataCache.CopyList(newVertices, m_Boundary);
            }

            var pose = boundedPlane.Pose;

            // The plane boundary points are in session space, but this <c>GameObject</c>
            // is already set to the plane's pose. We therefore want the boundary points
            // relative to the plane's pose, or plane-local space.
            pose.InverseTransformPositions(newVertices);
            var centerInPlaneSpace = pose.InverseTransformPosition(boundedPlane.Center);
            var normalInPlaneSpace = pose.InverseTransformDirection(boundedPlane.Normal);

            boundaryChanged(new ARPlaneBoundaryChangedEventArgs(this, centerInPlaneSpace, normalInPlaneSpace, newVertices));
        }

        internal void OnRemove()
        {
            if (removed != null)
                removed(this);

            if (destroyOnRemoval)
                Destroy(gameObject);
        }

        List<Vector3> m_Boundary;

        BoundedPlane m_BoundedPlane;

        TrackingState? m_TrackingState;
    }
}
