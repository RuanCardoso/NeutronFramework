﻿using NeutronNetwork.Attributes;
using NeutronNetwork.Client.Internal;
using NeutronNetwork.Internal.Attributes;
using System.Collections;
using UnityEngine;

namespace NeutronNetwork.Components
{
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Neutron/Neutron Animator")]
    public class NeutronAnimator : NeutronBehaviour
    {
        [Header("[Component]")]
        [ReadOnly] public Animator animator;

        [Header("[Parameters Settings]")]
        public NeutronAnimatorParameter[] parameters;

        [Header("[General Settings]")]
        [SerializeField] [Range(0, 1)] private float synchronizeInterval = 0.1f;
        [SerializeField] private SendTo sendTo = SendTo.Others;
        [SerializeField] private Broadcast broadcast = Broadcast.Room;
        [SerializeField] private Protocol protocol = Protocol.Udp;

        private void Start() { }

        public override void OnNeutronStart()
        {
            base.OnNeutronStart();
            if (HasAuthority)
                StartCoroutine(Synchronize());
        }

        private IEnumerator Synchronize()
        {
            while (true)
            {
                using (var options = Neutron.PooledNetworkWriters.Pull())
                {
                    options.SetLength(0);
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var networkedParameter = parameters[i];
                        if (networkedParameter.parameterMode == ParameterMode.NonSync) continue;
                        switch (networkedParameter.parameterType)
                        {
                            case AnimatorControllerParameterType.Float:
                                options.Write(animator.GetFloat(networkedParameter.parameterName));
                                break;
                            case AnimatorControllerParameterType.Int:
                                options.Write(animator.GetInteger(networkedParameter.parameterName));
                                break;
                            case AnimatorControllerParameterType.Bool:
                                options.Write(animator.GetBool(networkedParameter.parameterName));
                                break;
                            case AnimatorControllerParameterType.Trigger:
                                break;
                        }
                    }
                    iRPC(10018, options, CacheMode.Overwrite, sendTo, broadcast, protocol);
                }
                yield return new WaitForSeconds(synchronizeInterval);
            }
        }

        [iRPC(10018, true)]
        private void RPC(NeutronReader options, bool isMine, Player sender, NeutronMessageInfo infor)
        {
            using (options)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    var networkedParameter = parameters[i];
                    if (networkedParameter.parameterMode == ParameterMode.NonSync) continue;
                    switch (networkedParameter.parameterType)
                    {
                        case AnimatorControllerParameterType.Float:
                            animator.SetFloat(networkedParameter.parameterName, options.ReadSingle());
                            break;
                        case AnimatorControllerParameterType.Int:
                            animator.SetInteger(networkedParameter.parameterName, options.ReadInt32());
                            break;
                        case AnimatorControllerParameterType.Bool:
                            animator.SetBool(networkedParameter.parameterName, options.ReadBoolean());
                            break;
                        case AnimatorControllerParameterType.Trigger:
                            break;
                    }
                }
            }
        }
    }
}