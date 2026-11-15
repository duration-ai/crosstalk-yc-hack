using UnityEngine;

namespace Dogpatch.Look
{
    // Sits on the driven car's Rigidbody object and turns side contacts with walls, cars and props
    // into MotionFx.Scrape calls. Ground contacts (upward normals) are ignored.
    public sealed class ScrapeSensor : MonoBehaviour
    {
        float nextHit;

        void OnCollisionEnter(Collision collision) => Contact(collision, true);

        void OnCollisionStay(Collision collision) => Contact(collision, false);

        void Contact(Collision collision, bool impact)
        {
            if (collision.contactCount == 0) return;
            var contact = collision.GetContact(0);
            if (contact.normal.y > 0.6f) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < 2f) return;
            if (!impact && Time.time < nextHit) return;
            nextHit = Time.time + 0.06f;
            MotionFx.Scrape(contact.point, contact.normal, Mathf.Clamp01(speed / 18f) * (impact ? 1f : 0.6f));
        }
    }
}
