
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace MMMaellon.BeefSquashingFightingRing
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class BeefMoney : UdonSharpBehaviour
    {
        public ParticleSystem particles;
        [System.NonSerialized, UdonSynced, FieldChangeCallback(nameof(shoot))]
        public short _shoot;
        public short shoot
        {
            get => _shoot;
            set
            {
                _shoot = value;
                particles.Play();
            }
        }

        public override void OnPickup()
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        public override void OnPickupUseDown()
        {
            shoot = (short)((shoot + 1) % 1001);
            RequestSerialization();
        }

    }
}
