using UnityEngine;

namespace ScrapSiege.AR
{
    /// <summary>
    /// Scaffold for the Week 1 Cloud Anchor cross-device spike (plan.md Section 7).
    /// Not wired to ARCore Extensions yet — actual host/resolve calls get filled in
    /// once a second Android device is available for two-device testing.
    /// </summary>
    public class CloudAnchorManager : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Hosting,
            Hosted,
            Resolving,
            Resolved,
            Error
        }

        public State CurrentState { get; private set; } = State.Idle;
        public string CloudAnchorId { get; private set; }

        /// <summary>
        /// Host player: turn a local AR anchor into a Cloud Anchor other devices can resolve.
        /// TODO: call ARAnchorManagerExtensions.HostCloudAnchorAsync (ARCore Extensions)
        /// once the package is resolved and an ARAnchor exists to host.
        /// </summary>
        public void HostAnchor(ARAnchorStub localAnchor)
        {
            CurrentState = State.Hosting;
            Debug.Log("[CloudAnchorManager] HostAnchor stub called - not yet wired to ARCore Extensions.");
        }

        /// <summary>
        /// Joining player: resolve a Cloud Anchor ID shared by the host to sync to
        /// the same physical anchor point.
        /// TODO: call ARAnchorManagerExtensions.ResolveCloudAnchorIdAsync once wired.
        /// </summary>
        public void ResolveAnchor(string cloudAnchorId)
        {
            CloudAnchorId = cloudAnchorId;
            CurrentState = State.Resolving;
            Debug.Log($"[CloudAnchorManager] ResolveAnchor stub called for id '{cloudAnchorId}' - not yet wired.");
        }

        /// <summary>
        /// Placeholder until ARCore Extensions is resolved and we can reference the real ARAnchor type.
        /// </summary>
        public class ARAnchorStub { }
    }
}
