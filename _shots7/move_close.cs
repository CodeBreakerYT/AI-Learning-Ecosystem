var player = UnityEngine.GameObject.Find("PlayerPhysics");
var bow = UnityEngine.GameObject.Find("Bow Stand");
player.transform.position = bow.transform.position + new UnityEngine.Vector3(0.7f, 0.3f, -0.9f);
player.transform.rotation = UnityEngine.Quaternion.LookRotation(bow.transform.position - player.transform.position);
UnityEngine.Debug.Log("moved close to bow");
