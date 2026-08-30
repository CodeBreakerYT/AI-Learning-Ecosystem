var manager = UnityEngine.Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
var leftInteractor = UnityEngine.GameObject.Find("LeftHand").transform.Find("Left Direct Interactor").GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
var bowGO = UnityEngine.GameObject.Find("Bow Stand");
var bowGrab = bowGO.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

leftInteractor.transform.position = bowGO.transform.position;
manager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)leftInteractor, (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)bowGrab);
leftInteractor.transform.position += new UnityEngine.Vector3(0.3f, 0.2f, 0.1f);
UnityEngine.Debug.Log("moved hand");
