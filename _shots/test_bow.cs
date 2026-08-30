var manager = UnityEngine.Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
var rightInteractor = UnityEngine.GameObject.Find("RightHand").transform.Find("Right Direct Interactor").GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();

var gripGO = UnityEngine.GameObject.Find("Draw Grip");
var grab = gripGO.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

rightInteractor.transform.position = gripGO.transform.position;
manager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)rightInteractor, (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);
UnityEngine.Debug.Log("gripSelected=" + grab.isSelected);
