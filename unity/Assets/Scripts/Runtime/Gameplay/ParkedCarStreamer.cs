using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Every curb in Dogpatch has parked cars, thousands of them. Only those near the player are
    // switched on (rendered, simulated, seen by traffic and smash); the rest wait inactive where they
    // were left, a few hundred checked per frame with some hysteresis so cars at the edge don't flicker.
    public sealed class ParkedCarStreamer : MonoBehaviour
    {
        const float Radius = 150f;
        const float Hysteresis = 20f;
        const int PerFrame = 300;

        GameController game;
        readonly List<CarController> cars = new List<CarController>();
        int next;

        public void Init(GameController controller, IEnumerable<CarController> parked)
        {
            game = controller;
            cars.AddRange(parked);
            var focus = game.Focus != null ? game.Focus.position : Vector3.zero;
            foreach (var car in cars) Sweep(car, focus);
        }

        void Update()
        {
            if (cars.Count == 0 || game.Focus == null) return;
            var focus = game.Focus.position;
            for (int i = Mathf.Min(PerFrame, cars.Count); i > 0; i--)
            {
                next = (next + 1) % cars.Count;
                Sweep(cars[next], focus);
            }
        }

        void Sweep(CarController car, Vector3 focus)
        {
            if (car == null || car == game.Current || car.Driven) return;
            var d = car.transform.position - focus;
            float sq = d.x * d.x + d.z * d.z;
            bool active = car.gameObject.activeSelf;
            if (active && sq > (Radius + Hysteresis) * (Radius + Hysteresis)) car.gameObject.SetActive(false);
            else if (!active && sq < Radius * Radius) car.gameObject.SetActive(true);
        }
    }
}
