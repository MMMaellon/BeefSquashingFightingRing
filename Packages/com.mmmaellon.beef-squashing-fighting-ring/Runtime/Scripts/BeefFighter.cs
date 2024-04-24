
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MMMaellon.BeefSquashingFightingRing
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual), RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(Animator)), RequireComponent(typeof(VRCStation))]
    public class BeefFighter : UdonSharpBehaviour
    {
        [System.NonSerialized]
        public float damage = 1.0f;

        public BeefSquashingFightingRing fight_handler;
        public BeefWeapon weapon;
        public Transform start_transform;
        public VRCStation chair;
        public Rigidbody rigid;
        public CapsuleCollider capsule_collider;
        public Animator animator;
        public TextMeshProUGUI nameplate;

        [System.NonSerialized, UdonSynced]
        public Vector3 position;
        [System.NonSerialized, UdonSynced]
        public Vector3 velocity;
        [System.NonSerialized, UdonSynced]
        public float rotation;
        [System.NonSerialized, UdonSynced]
        public short attack_target = -1001;
        [System.NonSerialized, UdonSynced, FieldChangeCallback(nameof(attack_id))]
        public short _attack_id = 0;
        public short attack_id
        {
            get => _attack_id;
            set
            {
                _attack_id = value;
                weapon.PlayHitFX();
            }
        }
        [System.NonSerialized, UdonSynced]
        public Vector3 attack_vel;
        [Header("Raycasts for checking if you're grounded. Depends on size of your map, so play with these if you can't move")]
        public float grounded_check_height_offset = 0.04f;
        public float grounded_check_radius_offset = 0.95f;
        public float grounded_check_distance = 0.04f;
        public int id;
        public VRCPlayerApi vrc_player = null;
        [System.NonSerialized]
        public string last_player_name;
        [System.NonSerialized, UdonSynced, FieldChangeCallback(nameof(joined))]
        public bool _joined;
        public bool joined
        {
            get => _joined;
            set
            {
                _joined = value;
                SetVRCPlayer();
                SendCustomEventDelayedSeconds(nameof(SetVRCPlayer), Random.Range(1, 3));
            }
        }

        public void SetVRCPlayer()
        {
            if (fight_handler.local_fighter == this)
            {
                fight_handler.local_fighter = null;
            }
            if (joined)
            {
                vrc_player = Networking.GetOwner(gameObject);

                if (Utilities.IsValid(vrc_player))
                {
                    last_player_name = vrc_player.displayName;
                    nameplate.text = last_player_name;
                    if (vrc_player.isLocal)
                    {
                        fight_handler.local_fighter = this;
                    }
                }
                else
                {
                    last_player_name = "ERROR";
                }

                fight_handler.OnFighterJoin(this);
            }
            else
            {
                vrc_player = null;
                nameplate.text = "<i><size=8>Join";
                fight_handler.OnFighterLeft(this);
            }

        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player))
            {
                if (player.isLocal)
                {
                    joined = vrc_player == player;
                    RequestSerialization();
                }
            }
        }

        public void Join()
        {
            if (Utilities.IsValid(vrc_player) || fight_handler.state >= 0)
            {
                return;
            }
            vrc_player = Networking.LocalPlayer;
            Networking.SetOwner(vrc_player, gameObject);
            joined = true;
            RequestSerialization();
        }

        public void Leave()
        {
            if (!IsLocal())
            {
                return;
            }
            chair.ExitStation(vrc_player);
            RequestSerialization();
        }

        public void JoinLeave()
        {
            if (Utilities.IsValid(vrc_player))
            {
                if (vrc_player.isLocal)
                {
                    Leave();
                }
            }
            else if (!fight_handler.local_fighter)
            {
                Join();
            }
        }

        float max_height;
        float min_height;
        float height;
        public void OnGameStart()
        {
            if (!IsLocal())
            {
                Debug.LogError("Something messed up with the Beef Squashing Fighting Ring :(");
                return;
            }

            damage = fight_handler.starting_damage;
            vrc_player.TeleportTo(chair.transform.position, chair.transform.rotation);
            chair.UseStation(vrc_player);

            max_height = vrc_player.GetAvatarEyeHeightMaximumAsMeters();
            min_height = vrc_player.GetAvatarEyeHeightMinimumAsMeters();
            height = vrc_player.GetAvatarEyeHeightAsMeters();

            vrc_player.SetAvatarEyeHeightMaximumByMeters(fight_handler.fighter_height);
            vrc_player.SetAvatarEyeHeightMinimumByMeters(fight_handler.fighter_height);
            vrc_player.SetAvatarEyeHeightByMeters(fight_handler.fighter_height);
            weapon.sync.pickupable = true;
            input = Vector2.zero;
            turned = false;
            lookH = 0;
        }

        public void OnGameEnd()
        {
            if (IsLocal())
            {
                vrc_player.SetAvatarEyeHeightMaximumByMeters(max_height);
                vrc_player.SetAvatarEyeHeightMinimumByMeters(min_height);
                vrc_player.SetAvatarEyeHeightByMeters(height);
            }
            weapon.sync.Respawn();
            weapon.sync.pickupable = false;
            Leave();
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (IsLocal() && vrc_player == player)
            {
                OnGameEnd();
                joined = false;
                RequestSerialization();
            }
        }

        public bool IsLocal()
        {
            return Utilities.IsValid(vrc_player) && vrc_player.isLocal;
        }

        float syncTime = -1001f;
        public override void OnPreSerialization()
        {
            if (!Utilities.IsValid(vrc_player))
            {
                return;
            }
            syncTime = Time.timeSinceLevelLoad;
            position = transform.position;
            rotation = Vector3.SignedAngle(Vector3.forward, transform.rotation * Vector3.forward, Vector3.up);
            velocity = rigid.velocity;
        }

        Vector3 start_pos;
        Quaternion start_rot;
        Vector3 end_pos;
        Quaternion end_rot;
        short last_handled = -1001;
        public override void OnDeserialization()
        {
            start_pos = transform.position;
            start_rot = transform.rotation;
            start_vel = rigid.velocity;
            syncTime = Time.timeSinceLevelLoad;
            end_pos = position;
            end_rot = Quaternion.AngleAxis(rotation, Vector3.up);
            end_vel = velocity;

            if (attack_id != last_handled && fight_handler.local_fighter && fight_handler.local_fighter.id == attack_target)
            {
                last_handled = attack_id;
                fight_handler.local_fighter.OnAttacked(attack_vel);
            }
        }

        public float interpolation
        {
            get
            {
                // return lagTime <= 0 ? 1 : Mathf.Lerp(0, 1, (Time.timeSinceLevelLoad - syncTime) / lagTime);
                return (Time.timeSinceLevelLoad - syncTime) / networkUpdateInterval;
            }
        }
        [System.NonSerialized]
        public float networkUpdateInterval = 0.2f;

        Vector3 posControl1;
        Vector3 posControl2;
        Vector3 start_vel;
        Vector3 end_vel;

        public Vector3 HermiteInterpolatePosition()
        {//Shout out to Kit Kat for suggesting the improved hermite interpolation
            if (interpolation < 1)
            {
                posControl1 = start_pos + start_vel * interpolation / 3f;
                posControl2 = end_pos - end_vel * (1.0f - interpolation) / 3f;
                return Vector3.Lerp(Vector3.Lerp(posControl1, end_pos, interpolation), Vector3.Lerp(start_pos, posControl2, interpolation), interpolation);
            }
            return end_pos + end_vel * (interpolation - 1);
        }

        public void FixedUpdate()
        {
            if (fight_handler.state < 0 || !Utilities.IsValid(vrc_player))
            {
                return;
            }
            if (IsLocal())
            {
                MoveFighter();
                if (syncTime + networkUpdateInterval < Time.timeSinceLevelLoad && grounded)
                {
                    RequestSerialization();
                }
                if (transform.position.y < fight_handler.kill_floor.position.y || Vector3.Distance(transform.position, fight_handler.transform.position) > fight_handler.kill_distance)
                {
                    Leave();
                }
            }
            else if (interpolation <= 1)
            {
                transform.position = HermiteInterpolatePosition();
                transform.rotation = Quaternion.Slerp(start_rot, end_rot, interpolation);
                rigid.velocity = Vector3.Slerp(start_vel, end_vel, interpolation);
            }
        }

        BeefWeapon attacker;
        Vector3 temp_attack_vel;
        float last_hit;
        public void OnTriggerEnter(Collider collider)
        {
            if (!Utilities.IsValid(collider) || !joined)
            {
                return;
            }
            attacker = collider.GetComponentInParent<BeefWeapon>();
            if (!attacker || fight_handler.state < BeefSquashingFightingRing.STATE_NORMAL || fight_handler.state > BeefSquashingFightingRing.STATE_SUDDEN_DEATH || !fight_handler.local_fighter)
            {
                return;
            }
            if (IsLocal() || !attacker.sync.IsOwnerLocal() || last_hit + 0.2f > Time.timeSinceLevelLoad)
            {
                return;
            }
            last_hit = Time.timeSinceLevelLoad;
            // temp_attack_vel = attacker.CalcLinearRegressionOfVelocity();
            temp_attack_vel = attacker.LastVelocity();
            if (temp_attack_vel.magnitude < fight_handler.min_swing_vel)
            {
                return;
            }
            // temp_attack_vel += (transform.position - fight_handler.local_fighter.transform.position).normalized * temp_attack_vel.magnitude;
            temp_attack_vel *= attacker.base_attack_power + attacker.additional_buff;
            fight_handler.local_fighter.OnAttackOther(temp_attack_vel, this);
        }

        public void OnParticleCollision(GameObject other)
        {
            if (!other || fight_handler.state < 0)
            {
                return;
            }
            weapon.additional_buff += fight_handler.money_buff;
        }

        public void OnAttackOther(Vector3 new_attack_vel, BeefFighter new_attack_target)
        {
            attack_vel = new_attack_vel;
            attack_target = (short)new_attack_target.id;
            attack_id = (short)((attack_id + 1) % 3000);
            RequestSerialization();
        }

        float last_knock = -1001f;
        public void Knock(Vector3 knock_vel)
        {
            if (fight_handler.state >= BeefSquashingFightingRing.STATE_NORMAL && fight_handler.state <= BeefSquashingFightingRing.STATE_WINNER)
            {
                knock_vel.y = Mathf.Max(fight_handler.knock_back_height, fight_handler.knock_back_height + knock_vel.y);
                rigid.velocity = knock_vel;
                last_knock = Time.timeSinceLevelLoad;
                RequestSerialization();
            }
        }
        public void Jump(Vector3 jump_vel)
        {
            if (fight_handler.state >= BeefSquashingFightingRing.STATE_NORMAL && fight_handler.state <= BeefSquashingFightingRing.STATE_WINNER)
            {
                jump_vel.y = Mathf.Max(fight_handler.jump_height, fight_handler.jump_height + jump_vel.y);
                rigid.velocity = jump_vel;
                last_knock = Time.timeSinceLevelLoad;
                RequestSerialization();
            }
        }

        float last_attacked = -1001f;
        public void OnAttacked(Vector3 attack)
        {
            attack *= damage;
            last_attacked = Time.timeSinceLevelLoad;
            if (fight_handler.state == BeefSquashingFightingRing.STATE_NORMAL || fight_handler.state == BeefSquashingFightingRing.STATE_SUDDEN_DEATH)
            {
                Knock(attack);
            }
            damage += fight_handler.damage_per_attack;
        }

        VRCPlayerApi.TrackingData headData;
        Vector3 playerForward;
        Vector3 moveInfluence;
        Vector3 input;
        Quaternion moveRotation;
        public LayerMask floor_layer;
        const float RATE_OF_AIR_ACCELERATION = 5f;
        bool grounded;
        public void MoveFighter()
        {
            //Stolen from client sim
            // Physics.SyncTransforms();
            headData = vrc_player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            playerForward = headData.rotation * Vector3.forward;
            playerForward.y = 0;
            if (playerForward.magnitude <= 0)
            {
                playerForward = transform.rotation * Vector3.forward;
            }

            moveRotation = Quaternion.FromToRotation(Vector3.forward, playerForward);
            grounded = Physics.CapsuleCast(transform.position + Vector3.up * grounded_check_height_offset, transform.position + Vector3.up * (capsule_collider.height - grounded_check_height_offset), capsule_collider.radius * grounded_check_radius_offset, Vector3.down, grounded_check_distance, floor_layer);
            if (grounded && last_attacked + 0.5f < Time.timeSinceLevelLoad)
            {
                moveInfluence = (input.y * (moveRotation * Vector3.forward) + input.x * (moveRotation * Vector3.right)) * fight_handler.player_speed;
                if (jump)
                {
                    Jump(moveInfluence + Vector3.up * fight_handler.jump_height);
                    RequestSerialization();
                }
                else
                {
                    rigid.velocity = moveInfluence / 2;
                }
            }
            if (!grounded)
            {
                // Slowly add velocity from movement inputs
                // moveInfluence = Time.fixedDeltaTime * RATE_OF_AIR_ACCELERATION * new Vector3(input.x * fight_handler.player_speed, 0, input.y * fight_handler.player_speed);
                // moveInfluence.x = Mathf.Clamp(moveInfluence.x, -fight_handler.player_speed - rigid.velocity.x, fight_handler.player_speed - rigid.velocity.x);
                // moveInfluence.y = Mathf.Clamp(moveInfluence.y, -fight_handler.player_speed - rigid.velocity.y, fight_handler.player_speed - rigid.velocity.y);
                //
                // rigid.AddForce(moveRotation * moveInfluence, ForceMode.Acceleration);
            }

            HandleRotation();
        }

        float dif;
        float inputLookX;
        float lookH;
        public void HandleRotation()
        {
            if (vrc_player.IsUserInVR())
            {
                if (fight_handler.snap_turn)
                {
                    if (lookH < -0.5f)
                    {
                        transform.Rotate(Vector3.up * 30f);
                    }
                    else if (lookH > 0.5f)
                    {
                        transform.Rotate(Vector3.up * -30f);
                    }
                    lookH = 0;
                }
                else
                {
                    transform.Rotate(Vector3.up * lookH * 90f * 1.35f * Time.fixedDeltaTime);
                }
            }
            else
            {
                //Code shared by Centauri
                dif = Vector3.Dot(playerForward.normalized, transform.right);
                inputLookX = Input.GetAxisRaw("Mouse X");

                if (Mathf.Abs(dif) >= 0.89f && Mathf.Sign(dif) == Mathf.Sign(inputLookX))
                {
                    transform.localEulerAngles += 1.35f * inputLookX * Vector3.up;
                }
            }
            transform.rotation = Quaternion.FromToRotation(transform.rotation * Vector3.up, Vector3.up) * transform.rotation;
        }
        bool jump;
        public void OnJump(bool value)
        {
            jump = value;
            // if (value && grounded)
            // {
            //     moveInfluence = (input.y * (moveRotation * Vector3.forward) + input.x * (moveRotation * Vector3.right)) * fight_handler.player_speed;
            //     Knock(moveInfluence + Vector3.up * fight_handler.jump_height);
            // }
        }

        public void OnMoveVertical(float value)
        {
            input.y = value;
        }
        public void OnMoveHorizontal(float value)
        {
            input.x = value;
        }
        bool turned = false;
        public void OnLookHorizontal(float value)
        {
            // base.InputMoveHorizontal(value, args);
            if (fight_handler.snap_turn)
            {
                if (!turned && value < 0.5f)
                {
                    turned = true;
                }
                else
                {
                    if (value < -0.5f)
                    {
                        lookH = -1f;
                        turned = true;
                    }
                    else if (value > 0.5f)
                    {
                        lookH = 1f;
                        turned = true;
                    }
                    else
                    {
                        lookH = 0f;
                    }
                }
            }
            else
            {
                lookH = value;
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void Reset()
        {
            rigid = GetComponent<Rigidbody>();
            chair = GetComponent<VRCStation>();
            animator = GetComponent<Animator>();
        }
#endif
    }
}
