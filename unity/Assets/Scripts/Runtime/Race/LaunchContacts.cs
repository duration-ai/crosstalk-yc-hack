using Dogpatch.Gameplay;
using Dogpatch.Traffic;
using UnityEngine;

namespace Dogpatch.Race
{
    // Sits on each racer's car for the first seconds after GO and reports every car it touches: another
    // racer, traffic or a parked car. The launch should have none; the play-through checks the log.
    public sealed class LaunchContacts : MonoBehaviour
    {
        public RaceDirector Race;
        public float Until;

        void OnCollisionEnter(Collision collision)
        {
            if (Race == null || Time.time > Until) return;
            var other = collision.collider.GetComponentInParent<CarController>();
            var mine = GetComponent<CarController>();
            if (other == null || other == mine) return;
            bool racer = Race.IsRacerCar(other);
            if (racer && other.GetInstanceID() < mine.GetInstanceID()) return;
            Race.NoteLaunchContact(racer ? "racer" : other.GetComponent<TrafficAgent>() != null ? "traffic" : "parked", mine, other);
        }
    }
}
