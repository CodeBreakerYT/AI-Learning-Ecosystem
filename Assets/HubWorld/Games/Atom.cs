using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A grabbable, bondable atom sphere for ChemistryMoleculeGame's "grab and
    /// snap" molecule building - the physical replacement for its old
    /// "select the right atoms in a pool" mechanic. On release, checks for a
    /// nearby compatible atom (valence available on both sides, not already
    /// bonded to each other) and, if found, docks onto it: becomes kinematic
    /// and parents into a fixed offset, so bonded atoms move as one unit from
    /// then on rather than fighting each other's physics.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class Atom : MonoBehaviour
    {
        public string Element { get; private set; }
        public int MaxValence { get; private set; }
        public int UsedValence { get; private set; }
        public readonly List<Atom> Bonds = new List<Atom>();
        public bool CanBond => UsedValence < MaxValence;

        public Action<Atom, Atom, bool> onBondAttempt; // (this, other, success)
        public AudioClip bondSound;

        private const float BondRange = 0.55f;
        private const float BobHeight = 0.03f;
        private const float BobSpeed = 1.3f;
        private Rigidbody _rb;
        private XRGrabInteractable _grab;
        private float _bobPhase;
        private Vector3 _restPosition;
        private bool _isHeld;
        private bool _isDocked;

        public void Init(string element, int maxValence, Color color)
        {
            Element = element;
            MaxValence = maxValence;

            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null) renderer.material.color = color;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            labelGO.transform.localScale = Vector3.one * 0.3f;
            var label = labelGO.AddComponent<TextMeshPro>();
            label.text = element;
            label.fontSize = 6;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            // Levitating, not resting on a bench - no gravity, so a
            // half-let-go atom floats in place instead of dropping/rolling
            // off the table.
            _rb.useGravity = false;
            _restPosition = transform.position;
            _bobPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            _grab = GetComponent<XRGrabInteractable>();
            _grab.selectEntered.AddListener(_ => _isHeld = true);
            _grab.selectExited.AddListener(OnReleased);
        }

        private void OnDestroy()
        {
            if (_grab != null) _grab.selectExited.RemoveListener(OnReleased);
        }

        private void Update()
        {
            // Gentle floating bob while just sitting on the bench (not held,
            // not docked into a completed molecule) - "levitating in space"
            // instead of a static prop.
            if (_isHeld || _isDocked || _rb.isKinematic) return;
            var bob = Mathf.Sin(Time.time * BobSpeed + _bobPhase) * BobHeight;
            transform.position = new Vector3(_restPosition.x, _restPosition.y + bob, _restPosition.z);
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _isHeld = false;
            _restPosition = transform.position;

            if (transform.parent != null && transform.parent.GetComponent<Atom>() != null)
                return; // already docked onto something - don't re-scan while riding along as a child

            Atom nearest = null;
            float bestDist = BondRange;
            foreach (var candidate in FindObjectsByType<Atom>(FindObjectsSortMode.None))
            {
                if (candidate == this || Bonds.Contains(candidate)) continue;
                var dist = Vector3.Distance(transform.position, candidate.transform.position);
                if (dist < bestDist)
                {
                    nearest = candidate;
                    bestDist = dist;
                }
            }

            if (nearest == null) return;

            if (CanBond && nearest.CanBond)
                TryBond(nearest);
            else
                onBondAttempt?.Invoke(this, nearest, false);
        }

        // Fixed, symmetric bond directions per hub valence (2/3/4 slots) -
        // deterministic molecular geometry instead of "wherever the live
        // hand position happened to be at the exact release frame," which
        // produced inconsistent, sometimes-overlapping bond placement and
        // (with two atoms' independent bob animations both still running
        // right up to the release instant) a visibly jittery snap. A fixed
        // direction table also means the shape reads as a real molecule
        // (e.g. two hydrogens spread apart around oxygen) rather than
        // whatever the grab happened to look like.
        private static readonly Vector3[] Slots2 = { Vector3.right, Vector3.left };
        private static readonly Vector3[] Slots3 =
        {
            new Vector3(1f, 0f, 0f), new Vector3(-0.5f, 0f, 0.866f), new Vector3(-0.5f, 0f, -0.866f)
        };
        private static readonly Vector3[] Slots4 =
        {
            new Vector3(1, 1, 1).normalized, new Vector3(-1, -1, 1).normalized,
            new Vector3(-1, 1, -1).normalized, new Vector3(1, -1, -1).normalized
        };

        private static Vector3 SlotDirection(int maxValence, int slotIndex)
        {
            var slots = maxValence switch { 2 => Slots2, 3 => Slots3, 4 => Slots4, _ => Slots2 };
            return slots[slotIndex % slots.Length];
        }

        private void TryBond(Atom other)
        {
            // Slot index BEFORE incrementing - "this is other's Nth bond."
            var otherSlot = other.UsedValence;

            UsedValence++;
            other.UsedValence++;
            Bonds.Add(other);
            other.Bonds.Add(this);

            // Dock this atom onto the other at a fixed offset so the pair moves
            // as one rigid unit - simpler and more robust for a VR prototype
            // than tuning a physics joint, and reads identically to the player.
            _isDocked = true;
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            // Colliders no longer needed once docked - a lingering kinematic
            // collider overlapping its (still non-kinematic, still bobbing)
            // parent's own collider fights the physics solver every step,
            // which is what the reported "glitching around" actually was.
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;

            var dockOffset = SlotDirection(other.MaxValence, otherSlot) * 0.4f;
            transform.SetParent(other.transform, true);
            transform.localPosition = dockOffset;
            transform.localRotation = Quaternion.identity;

            SpawnBondStick(other);

            var clip = bondSound != null ? bondSound : other.bondSound;
            if (clip != null) PlayClip2D(clip);

            onBondAttempt?.Invoke(this, other, true);
        }

        // AudioSource.PlayClipAtPoint spawns a temp object using the CLIP's
        // own default 3D/rolloff import settings - easy to end up inaudible
        // depending on listener distance ("no sound on attachment" was
        // this). Forcing a plain 2D one-shot guarantees it's heard.
        private static void PlayClip2D(AudioClip clip)
        {
            var go = new GameObject("Bond Sound");
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 0f;
            source.volume = 0.8f;
            source.Play();
            UnityEngine.Object.Destroy(go, clip.length + 0.1f);
        }

        private void SpawnBondStick(Atom other)
        {
            var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stick.name = "Bond";
            Destroy(stick.GetComponent<Collider>());
            stick.transform.SetParent(other.transform, true);
            var mid = (transform.position + other.transform.position) / 2f;
            stick.transform.position = mid;
            stick.transform.up = (transform.position - other.transform.position).normalized;
            var length = Vector3.Distance(transform.position, other.transform.position);
            stick.transform.localScale = new Vector3(0.05f, length / 2f, 0.05f);
            stick.GetComponent<Renderer>().material.color = new Color(0.8f, 0.8f, 0.85f);
        }
    }
}
