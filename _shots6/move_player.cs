var player = UnityEngine.GameObject.Find("PlayerPhysics");
var cannon = UnityEngine.GameObject.Find("Cannon Body");
player.transform.position = cannon.transform.position + new UnityEngine.Vector3(3f, 1.2f, -3.5f);
player.transform.rotation = UnityEngine.Quaternion.LookRotation(cannon.transform.position - player.transform.position);
UnityEngine.Debug.Log("player moved to " + player.transform.position);
