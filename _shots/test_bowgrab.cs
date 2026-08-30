var manager = UnityEngine.Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
var leftInteractor = UnityEngine.GameObject.Find("LeftHand").transform.Find("Left Direct Interactor").GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();

var bowGO = UnityEngine.GameObject.Find("Bow Stand");
var bowGrab = bowGO.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
UnityEngine.Debug.Log("bowGrab found=" + (bowGrab != null) + " hasCollider=" + (bowGO.GetComponent<UnityEngine.Collider>() != null));

leftInteractor.transform.position = bowGO.transform.position;
manager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)leftInteractor, (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)bowGrab);
UnityEngine.Debug.Log("bowSelected=" + bowGrab.isSelected);

// Move the hand and confirm the bow (and everything attached to it) follows.
var beforePos = bowGO.transform.position;
leftInteractor.transform.position += new UnityEngine.Vector3(0.3f, 0.2f, 0.1f);
