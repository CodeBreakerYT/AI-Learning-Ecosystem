using Convai.Scripts.Runtime.Features;
using UnityEngine;
using UnityEngine.AI;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Alternative to TeacherWander for a teacher who should actively
    /// accompany the player through a scene instead of idling near her own
    /// spawn point - continuously paths to the player's position on the
    /// scene's baked NavMesh, keeping a stopping distance so she doesn't
    /// crowd them, and stops pathing while a real Convai action is playing
    /// (same handoff rule TeacherWander uses).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    public class TeacherFollowPlayer : MonoBehaviour
    {
        public float followDistance = 2.2f;
        public float repathInterval = 0.35f;

        private NavMeshAgent _agent;
        private Animator _animator;
        private ConvaiActionsHandler _actionsHandler;
        private Transform _player;
        private bool _convaiBusy;
        private float _nextRepathTime;
        private bool _wasMoving;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponent<Animator>();
            _actionsHandler = GetComponent<ConvaiActionsHandler>();
            _agent.stoppingDistance = followDistance;
        }

        private void Start()
        {
            if (_actionsHandler != null)
                _actionsHandler.RegisterForActionEvents(OnConvaiActionStarted, OnConvaiActionEnded);

            var playerGO = GameObject.FindGameObjectWithTag("Player");
            _player = playerGO != null ? playerGO.transform : (Camera.main != null ? Camera.main.transform : null);
        }

        private void OnDestroy()
        {
            if (_actionsHandler != null)
                _actionsHandler.UnregisterForActionEvents(OnConvaiActionStarted, OnConvaiActionEnded);
        }

        private void OnConvaiActionStarted(string action, GameObject target)
        {
            _convaiBusy = true;
            _agent.ResetPath();
        }

        private void OnConvaiActionEnded(string action, GameObject target)
        {
            _convaiBusy = false;
        }

        private void Update()
        {
            if (_player == null || _convaiBusy) return;

            if (Time.time >= _nextRepathTime)
            {
                _nextRepathTime = Time.time + repathInterval;
                if (NavMesh.SamplePosition(_player.position, out var hit, 3f, NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
            }

            bool moving = !_agent.pathPending && _agent.remainingDistance > _agent.stoppingDistance + 0.05f;
            if (moving)
            {
                if (_agent.velocity.sqrMagnitude > 0.01f)
                {
                    var rot = Quaternion.LookRotation(_agent.velocity.normalized);
                    rot.x = 0f;
                    rot.z = 0f;
                    transform.rotation = Quaternion.Slerp(transform.rotation, rot, 6f * Time.deltaTime);
                }
                if (!_wasMoving) _animator.CrossFadeInFixedTime(Animator.StringToHash("Walking"), 0.1f);
            }
            else
            {
                if (_wasMoving) _animator.CrossFadeInFixedTime(Animator.StringToHash("Idle"), 0.1f);

                var toPlayer = _player.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.05f)
                {
                    var rot = Quaternion.LookRotation(toPlayer.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, rot, 4f * Time.deltaTime);
                }
            }
            _wasMoving = moving;
        }
    }
}
