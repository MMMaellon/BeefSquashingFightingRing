
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using MMMaellon;
using VRC.Udon.Common;

namespace MMMaellon.BokFights
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual), RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(Animator)), RequireComponent(typeof(SmartObjectSync))]
    public class BokWeapon : UdonSharpBehaviour
    {
        public Rigidbody rigid;
        public Animator animator;
        public SmartObjectSync sync;
        public Transform vel_center;
        public BokFighter fighter;
        public ParticleSystem hitParticles;
        public AudioSource hitSound;
        [System.NonSerialized, UdonSynced, FieldChangeCallback(nameof(slashes))]
        public bool _slashes;
        public bool slashes
        {
            get => _slashes;
            set
            {
                _slashes = value;
                animator.SetTrigger("slash");
                if (sync.IsLocalOwner())
                {
                    RequestSerialization();
                }
            }
        }

        int record_length = 0;
        int record_index = 0;
        Vector3 last_position;
        Vector3[] recorded_velocities = new Vector3[64];
        float[] recorded_deltas = new float[64];
        float[] recorded_times = new float[64];

        int last_loop_frame = -1001;
        public void Loop()
        {
            if (last_loop_frame >= Time.frameCount)
            {
                Debug.LogWarning("stop loop");
                return;
            }
            last_loop_frame = Time.frameCount;
            SendCustomEventDelayedFrames(nameof(Loop), 1);
            record_index = (record_index + 1) % recorded_velocities.Length;
            record_length++;
            recorded_velocities[record_index] = (vel_center.position - last_position) / Time.deltaTime;
            recorded_deltas[record_index] = Time.deltaTime;
            recorded_times[record_index] = Time.timeSinceLevelLoad;
            last_position = vel_center.position;
        }

        public override void OnPickup()
        {
            record_length = 0;
            last_position = vel_center.position;
            SendCustomEventDelayedFrames(nameof(Loop), 1);
        }

        public override void OnDrop()
        {
            last_loop_frame = Time.frameCount + 2;
        }

        public override void OnPickupUseDown()
        {
            slashes = !slashes;
        }

        public void PlayHitFX()
        {
            if (hitParticles)
            {
                hitParticles.Play();
            }
            if (hitSound)
            {
                hitSound.Play();
            }
        }

        public void ReadRecord(float duration, out Vector3[] velocities, out float[] times)
        {
            var array_length = record_length;
            var index = record_index;
            for (int i = 0; i < Mathf.Min(record_length, recorded_times.Length); i++)
            {
                index = (record_index + recorded_times.Length - i) % recorded_times.Length;
                if (Time.timeSinceLevelLoad - recorded_times[index] >= duration)
                {
                    array_length = i;
                    break;
                }
            }

            if (array_length <= 0)
            {
                //duration was too short. just return the current velocity
                velocities = new Vector3[1];
                times = new float[1];
                velocities[0] = sync.rigid.velocity;
                times[0] = Time.deltaTime;
                return;
            }
            velocities = new Vector3[array_length];
            times = new float[array_length];
            index = record_index - array_length;
            for (int i = 0; i < array_length; i++)
            {
                index++;
                if (index < 0)
                {
                    index += recorded_velocities.Length;
                }
                else if (index >= recorded_velocities.Length)
                {
                    index -= recorded_velocities.Length;
                }
                velocities[i] = recorded_velocities[index];
                times[i] = recorded_deltas[index];
            }
        }

        //from Mahu
        public Vector3 CalcLinearRegressionOfVelocity()
        {
            //Taken from Mahu's AxeThrowing: https://github.com/mahuvrc/VRCAxeThrowing/blob/d90da07893a9a2006a6a21132cb25646fa78c557/Assets/mahu/axe-throwing/scripts/ThrowingAxe.cs#L600-L637

            Vector3[] y;
            float[] x;
            ReadRecord(0.2f, out y, out x);//read the record of velocities for the last 0.2 seconds
            Debug.LogWarning("Y:");
            foreach (Vector3 v in y)
            {
                Debug.LogWarning("- " + v.ToString());
            }

            Debug.LogWarning("X:");

            foreach (float f in x)
            {
                Debug.LogWarning("- " + f.ToString());
            }
            // this is linear regression via the least squares method. it's loads better
            // than averaging the velocity frame to frame especially at low frame rates
            // and will smooth out noise caused by fluctuations frame to frame and the
            // tendency for players to sharply flick their wrist when throwing

            float sumx = 0;                      /* sum of x     */
            float sumx2 = 0;                     /* sum of x**2  */
            Vector3 sumxy = Vector3.zero;                     /* sum of x * y */
            Vector3 sumy = Vector3.zero;                      /* sum of y     */
            Vector3 sumy2 = Vector3.zero;                     /* sum of y**2  */
            int n = x.Length;

            for (int i = 0; i < n; i++)
            {
                var xi = x[i];
                var yi = y[i];
                sumx += xi;
                sumx2 += xi * xi;
                sumxy += xi * yi;
                sumy += yi;
                sumy2 += Vector3.Scale(yi, yi);
            }

            float denom = n * sumx2 - sumx * sumx;
            if (denom == 0)
            {
                // singular matrix. can't solve the problem.
                return y[n - 1];
            }


            Vector3 m = (n * sumxy - sumx * sumy) / denom;
            Vector3 b = (sumy * sumx2 - sumx * sumxy) / denom;

            return m * x[n - 1] + b;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void Reset()
        {
            rigid = GetComponent<Rigidbody>();
            animator = GetComponent<Animator>();
            sync = GetComponent<SmartObjectSync>();
            fighter = GetComponentInParent<BokFighter>();
        }
#endif
    }
}
