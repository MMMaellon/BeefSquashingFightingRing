
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Enums;

namespace MMMaellon.BeefSquashingFightingRing
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual), RequireComponent(typeof(Animator))]
    public class BeefSquashingFightingRing : UdonSharpBehaviour
    {
        public Animator animator;
        public Transform kill_floor;
        public float kill_distance = 10f;
        public Text winner_name;
        [System.NonSerialized, UdonSynced, FieldChangeCallback(nameof(state))]
        public short _state = -1001;
        short last_state = -1001;
        float last_state_change;
        public short state
        {
            get => _state;
            set
            {
                last_state = _state;
                last_state_change = Time.timeSinceLevelLoad;
                _state = value;
                OnStateChange();
                if (local_fighter)
                {
                    if (last_state < 0 && value >= 0)
                    {
                        local_fighter.OnGameStart();
                    }
                    else if (last_state >= 0 && value < 0)
                    {
                        local_fighter.OnGameEnd();
                    }
                }
                if (Networking.LocalPlayer.IsOwner(gameObject))
                {
                    RequestSerialization();
                }


                animator.SetInteger("state", value);
            }
        }
        VRCPlayerApi winner;
        [System.NonSerialized, UdonSynced, FieldChangeCallback(nameof(winner_id))]
        public short _winner_id = -1001;
        public short winner_id
        {
            get => _winner_id;
            set
            {
                _winner_id = value;
                winner = VRCPlayerApi.GetPlayerById(value);
                if (Utilities.IsValid(winner))
                {
                    winner_name.text = winner.displayName + " Wins!";
                }
                else
                {
                    winner_name.text = "";
                }
                if (Networking.LocalPlayer.IsOwner(gameObject))
                {
                    RequestSerialization();
                }
            }
        }

        public float fight_duration = 60f;
        public float starting_damage = 0.2f;
        public float damage_per_attack = 0.03f;
        public float fighter_height = 0.2f;
        public float jump_height = 1f;
        public float knock_back_height = 1.5f;
        public float player_speed = 1f;
        public float min_swing_vel = 0.5f;
        public float money_buff = 0.05f;
        public bool snap_turn = false;
        public AudioSource sfx;
        public AudioClip round_start;
        public AudioClip sudden_death;
        public AudioClip win_sound;

        public const short STATE_STOPPED = -1001;
        public const short STATE_NORMAL = 0;
        public const short STATE_SUDDEN_DEATH = 1;
        public const short STATE_WINNER = 2;

        public BeefFighter[] fighters;
        public BeefWeapon[] sudden_death_weapons;

        [System.NonSerialized]
        public BeefFighter local_fighter;

        bool first_enable = true;
        public void OnEnable()
        {
            if (first_enable)
            {
                first_enable = false;
            }
        }

        public void OnStateChange()
        {
            switch (state)
            {
                case STATE_NORMAL:
                    {
                        SendCustomEventDelayedFrames(nameof(Loop), 2, EventTiming.Update);
                        foreach (BeefFighter f in fighters)
                        {
                            f.rigid.isKinematic = false;
                            f.transform.localPosition = Vector3.zero;
                            f.transform.localRotation = Quaternion.identity;
                            f.weapon.transform.localPosition = Vector3.zero;
                            f.weapon.transform.localRotation = Quaternion.identity;
                            f.weapon.gameObject.SetActive(true);
                            f.weapon.additional_buff = 0;
                            if (f.IsLocal())
                            {
                                f.weapon.sync.TakeOwnership(true);
                            }
                        }
                        foreach (BeefWeapon weapon in sudden_death_weapons)
                        {
                            weapon.gameObject.SetActive(false);
                        }
                        sfx.clip = round_start;
                        sfx.Play();
                        break;
                    }
                case STATE_SUDDEN_DEATH:
                    {
                        SendCustomEventDelayedFrames(nameof(Loop), 2, EventTiming.Update);
                        foreach (BeefFighter f in fighters)
                        {
                            f.rigid.isKinematic = false;
                            f.weapon.gameObject.SetActive(true);
                            f.weapon.additional_buff += 0.1f;
                        }
                        foreach (BeefWeapon weapon in sudden_death_weapons)
                        {
                            weapon.transform.localPosition = Vector3.zero;
                            weapon.transform.localRotation = Quaternion.identity;
                            weapon.gameObject.SetActive(true);
                        }
                        sfx.clip = sudden_death;
                        sfx.Play();
                        break;
                    }
                case STATE_WINNER:
                    {
                        if (Networking.LocalPlayer.IsOwner(gameObject))
                        {
                            winner_id = -1001;
                        }
                        foreach (BeefFighter f in fighters)
                        {
                            f.rigid.isKinematic = false;
                            f.weapon.gameObject.SetActive(true);
                            if (Networking.LocalPlayer.IsOwner(gameObject) && Utilities.IsValid(f.vrc_player))
                            {
                                winner_id = (short)f.vrc_player.playerId;
                            }
                        }
                        sfx.clip = win_sound;
                        sfx.Play();
                        break;
                    }
                default:
                    {
                        foreach (BeefFighter f in fighters)
                        {
                            f.rigid.isKinematic = true;
                            f.weapon.gameObject.SetActive(false);
                        }
                        break;
                    }
            }
        }

        int last_loop_frame = -1001;
        int timer_number;
        public void Loop()
        {
            if (state < 0 || last_loop_frame >= Time.frameCount)
            {
                return;
            }
            last_loop_frame = Time.frameCount;
            SendCustomEventDelayedFrames(nameof(Loop), 1, EventTiming.Update);
            switch (state)
            {
                case STATE_NORMAL:
                    {
                        timer_number = Mathf.CeilToInt(last_state_change + fight_duration - Time.timeSinceLevelLoad);
                        if (Networking.LocalPlayer.IsOwner(gameObject))
                        {
                            if (timer_number <= 0)
                            {
                                state = STATE_SUDDEN_DEATH;
                            }
                            if (joined_fighter_count <= 1)
                            {
                                state = STATE_WINNER;
                            }
                        }
                        break;
                    }
                case STATE_SUDDEN_DEATH:
                    {
                        if (Networking.LocalPlayer.IsOwner(gameObject))
                        {
                            if (joined_fighter_count <= 1)
                            {
                                state = STATE_WINNER;
                            }
                        }
                        break;
                    }
                case STATE_WINNER:
                    {
                        if (Networking.LocalPlayer.IsOwner(gameObject))
                        {
                            if (joined_fighter_count <= 0)
                            {
                                state = STATE_STOPPED;
                            }
                        }
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }

        [System.NonSerialized]
        public int joined_fighter_count = 0;
        public void OnFighterJoin(BeefFighter fighter)
        {
            joined_fighter_count = 0;
            foreach (BeefFighter f in fighters)
            {
                if (f.joined)
                {
                    joined_fighter_count++;
                }
            }
            Debug.LogWarning("new joined count: " + joined_fighter_count);
        }

        public void OnFighterLeft(BeefFighter fighter)
        {
            joined_fighter_count = 0;
            foreach (BeefFighter f in fighters)
            {
                if (f.joined)
                {
                    joined_fighter_count++;
                }
            }
            Debug.LogWarning("new joined count: " + joined_fighter_count);
        }

        public void StartGame()
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            state = STATE_NORMAL;
        }

        public override void InputJump(bool value, UdonInputEventArgs args)
        {
            if (local_fighter)
            {
                local_fighter.OnJump(value);
            }
        }
        public override void InputMoveVertical(float value, UdonInputEventArgs args)
        {
            if (local_fighter)
            {
                local_fighter.OnMoveVertical(value);
            }
        }
        public override void InputMoveHorizontal(float value, UdonInputEventArgs args)
        {
            if (local_fighter)
            {
                local_fighter.OnMoveHorizontal(value);
            }
        }
        public override void InputLookHorizontal(float value, UdonInputEventArgs args)
        {

            if (local_fighter)
            {
                local_fighter.OnLookHorizontal(value);
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void OnValidate()
        {
            HashSet<BeefFighter> hash = new HashSet<BeefFighter>(fighters);
            hash.Remove(null);
            fighters = hash.ToArray();
            for (int i = 0; i < fighters.Length; i++)
            {
                fighters[i].id = i;
            }
        }
        public void Reset()
        {
            animator = GetComponent<Animator>();
        }
#endif
    }
}
