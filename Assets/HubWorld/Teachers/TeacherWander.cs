using System.Collections;
using Convai.Scripts.Runtime.Features;
using UnityEngine;
using UnityEngine.AI;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Keeps a teacher NPC from standing still: periodically wanders to a random
    /// point around its classroom on the same NavMeshAgent/Animator that
    /// ConvaiActionsHandler's MoveTo/PickUp/etc. actions drive. Pauses itself
    /// whenever a real Convai action is in progress (tracked via
    /// RegisterForActionEvents) so the two never fight over the agent's
    /// destination - wandering is the "nothing better to do" fallback, Convai's
    /// own actions always take priority.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    public class TeacherWander : MonoBehaviour
    {
        public float wanderRadius = 4f;
        public float minPauseSeconds = 3f;
        public float maxPauseSeconds = 7f;

        private NavMeshAgent _agent;
        private Animator _animator;
        private ConvaiActionsHandler _actionsHandler;
        private Vector3 _wanderCenter;
        private bool _convaiBusy;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponent<Animator>();
            _actionsHandler = GetComponent<ConvaiActionsHandler>();
            _wanderCenter = transform.position;
        }

        private void Start()
        {
            if (_actionsHandler != null)
                _actionsHandler.RegisterForActionEvents(OnConvaiActionStarted, OnConvaiActionEnded);
            StartCoroutine(WanderLoop());
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

        private IEnumerator WanderLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(minPauseSeconds, maxPauseSeconds));
                if (_convaiBusy) continue;

                Vector3 candidate = _wanderCenter + Random.insideUnitSphere * wanderRadius;
                candidate.y = _wanderCenter.y;
                if (!NavMesh.SamplePosition(candidate, out var hit, wanderRadius, NavMesh.AllAreas)) continue;
                if (_convaiBusy) continue;

                _animator.applyRootMotion = false;
                _agent.updateRotation = false;
                _animator.CrossFadeInFixedTime(Animator.StringToHash("Walking"), 0.1f);
                _agent.SetDestination(hit.position);

                while (_agent.pathPending) yield return null;

                while (!_convaiBusy && _agent.remainingDistance > _agent.stoppingDistance)
                {
                    if (_agent.velocity.sqrMagnitude > 0.01f)
                    {
                        var rot = Quaternion.LookRotation(_agent.velocity.normalized);
                        rot.x = 0f;
                        rot.z = 0f;
                        transform.rotation = Quaternion.Slerp(transform.rotation, rot, 5f * Time.deltaTime);
                    }
                    yield return null;
                }

                _animator.applyRootMotion = true;
                if (!_convaiBusy)
                    _animator.CrossFadeInFixedTime(Animator.StringToHash("Idle"), 0.1f);
            }
        }
    }
}
