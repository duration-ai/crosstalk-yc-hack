using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Audio
{
    // Footsteps by surface, the jump grunt and the landing thud for the walking player.
    public sealed class PlayerSounds : MonoBehaviour
    {
        PlayerController player;
        CharacterController body;
        Vector3 last;
        float travelled;
        bool left;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            body = GetComponent<CharacterController>();
            last = transform.position;
        }

        void OnEnable()
        {
            if (player == null) return;
            player.Jumped += OnJumped;
            player.Landed += OnLanded;
            last = transform.position;
        }

        void OnDisable()
        {
            if (player == null) return;
            player.Jumped -= OnJumped;
            player.Landed -= OnLanded;
        }

        void Update()
        {
            var p = transform.position;
            var step = p - last;
            last = p;
            step.y = 0f;
            if (body == null || !body.isGrounded || step.magnitude > 2f) return;
            travelled += step.magnitude;
            float speed = player.Speed;
            float stride = Mathf.Lerp(0.7f, 1.5f, Mathf.InverseLerp(1f, 7f, speed));
            if (speed < 0.3f || travelled < stride) return;
            travelled = 0f;
            left = !left;
            Step(Mathf.Lerp(0.35f, 0.8f, speed / 7f));
        }

        void Step(float volume)
        {
            var ground = transform.position.y > 0.6f ? Ground.Metal : SurfaceMap.At(transform.position);
            if (GameEvents.Rain > 0.4f && ground != Ground.Grass && ground != Ground.Metal) ground = Ground.Water;
            string clip = "step_" + ground.ToString().ToLowerInvariant();
            SoundSystem.Play(clip, transform.position, Bus.Sfx, volume, Random.Range(0.92f, 1.08f) * (left ? 1f : 0.97f), 150, 25f);
        }

        void OnJumped() => SoundSystem.Play("jump_hup", transform.position + Vector3.up * 1.5f, Bus.Sfx, 0.75f, Random.Range(0.95f, 1.08f), 180, 25f);

        void OnLanded(float fallSpeed)
        {
            float v = Mathf.InverseLerp(6f, 16f, fallSpeed);
            SoundSystem.Play("land", transform.position, Bus.Sfx, 0.25f + 0.55f * v, Random.Range(0.92f, 1.05f), 170, 25f);
            Step(0.45f);
        }
    }
}
