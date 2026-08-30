using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// One real, physical arrow the player can grab off their back (see
    /// ArcheryQuiver) and nock onto the bow themselves - a genuinely separate
    /// object from the bow, not a bead permanently welded to it. Right-hand
    /// only, matching real archery form (left hand holds the bow, right hand
    /// draws). While held and not yet nocked, checks its own distance to the
    /// bow's nock point every frame; get close enough and it snaps onto the
    /// string, handing control of its position over to ArcheryBow's own draw
    /// math until release.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class QuiverArrow : MonoBehaviour
    {
        public ArcheryBow bow;
        /// <summary>Fired the moment this arrow is first grabbed - the quiver listens for this to spawn a replacement in the empty slot.</summary>
        public Action<QuiverArrow> onTakenFromQuiver;

        private XRGrabInteractable _grab;
        private Rigidbody _rb;
        private bool _nocked;
        private bool _takenNotified;

        private void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.isKinematic = true; // sits still in the quiver until grabbed

            ArcheryBow.RestrictToHand(_grab, wantLeft: false);
            _grab.selectEntered.AddListener(OnGrabbed);
            _grab.selectExited.AddListener(OnReleased);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            if (_takenNotified) return;
            _takenNotified = true;
            onTakenFromQuiver?.Invoke(this);
            _rb.useGravity = false; // stays weightless in hand until nocked/fired, same as the old fixed-grip arrow
        }

        private void Update()
        {
            if (_nocked || bow == null || !_grab.isSelected) return;
            if (Vector3.Distance(transform.position, bow.nockAnchor.position) > bow.nockRadius) return;

            if (bow.TryNock(transform, _grab))
            {
                _nocked = true;
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            if (_nocked)
            {
                bow.ReleaseNockedArrow(); // fires (or drops, if not drawn far enough) via ArcheryBow
            }
            else
            {
                // Let go without ever nocking it - a normal dropped prop.
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }
        }
    }
}
