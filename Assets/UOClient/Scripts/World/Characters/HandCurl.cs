using System.Collections.Generic;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// Closes a Sidekick hand into a fist around what it holds. The locomotion clips leave the hands relaxed and open,
    /// so after the Animator poses them each frame this bends the finger joints further about their curl axis
    /// (local Z on the Sidekick rig) and the thumb across.
    /// </summary>
    public sealed class HandCurl : MonoBehaviour
    {
        // Extra bend per joint, degrees: knuckle, middle, tip; the thumb only at its middle and tip.
        private const float Knuckle = -55f, Middle = -55f, Tip = -40f, Thumb = -25f;

        private Transform[] _bones = new Transform[0];
        private float[] _angles = new float[0];

        /// <summary>Curls the fingers of the hand whose bones end in `side` ("_r" or "_l") on this model.</summary>
        public static void Close(GameObject model, string side)
        {
            HandCurl curl = model.GetComponent<HandCurl>();
            if (curl == null)
            {
                curl = model.AddComponent<HandCurl>();
            }

            var bones = new List<Transform>(curl._bones);
            var angles = new List<float>(curl._angles);
            int before = bones.Count;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.EndsWith(side) || bones.Contains(t))
                {
                    continue;
                }

                float angle = 0f;
                if (t.name.StartsWith("index") || t.name.StartsWith("middle") || t.name.StartsWith("ring") || t.name.StartsWith("pinky"))
                {
                    angle = t.name.Contains("_01_") ? Knuckle : t.name.Contains("_02_") ? Middle : t.name.Contains("_03_") ? Tip : 0f;
                }
                else if (t.name.StartsWith("thumb_02") || t.name.StartsWith("thumb_03"))
                {
                    angle = Thumb;
                }

                if (angle != 0f)
                {
                    bones.Add(t);
                    angles.Add(angle);
                }
            }

            curl._bones = bones.ToArray();
            curl._angles = angles.ToArray();
            if (!Application.isPlaying)
            {
                // Edit-mode previews have no frame loop: bend the newly added joints once.
                for (int i = before; i < curl._bones.Length; i++)
                {
                    curl._bones[i].localRotation *= Quaternion.Euler(0f, 0f, curl._angles[i]);
                }
            }
        }

        private void LateUpdate()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null)
                {
                    _bones[i].localRotation *= Quaternion.Euler(0f, 0f, _angles[i]);
                }
            }
        }
    }
}
