using UnityEngine;
using Design;
using System.Collections.Generic;

namespace Game.View
{
    public class DJ_CameraIndicator : MonoBehaviour
    {
        [SerializeField]
        private GameObject TrackerPrefab;

        private DJ_CameraControl CameraControl;

        private Dictionary<int, FighterView> NotVisibleFighters =
            new Dictionary<int, FighterView>();

        private Dictionary<int, GameObject> Trackers =
            new Dictionary<int, GameObject>();
        private Camera Cam;
        void Start()
        {
            CameraControl = this.gameObject.GetComponent<DJ_CameraControl>();
            Cam = this.GetComponent<Camera>();
        }

        public void Track(FighterView[] _fighters)
        {
            NotVisibleFighters.Clear();

            //Adding all nonvisible fighters as key value pairs, fighter number -> FighterView
            //Key value pairs are for if in the future, you would want to add different trackers for each fighter
            for (int i = 0; i < _fighters.Length; i++)
            {
                //Detecting if visible or not
                
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(Cam);
                bool v = GeometryUtility.TestPlanesAABB(planes, _fighters[i].gameObject.GetComponent<Renderer>().bounds);
                if (!v)
                {
                    NotVisibleFighters.Add(i, _fighters[i]);
                }
            }

            //Creating/updating trackers for nonvisible fighters
            foreach (KeyValuePair<int, FighterView> f in NotVisibleFighters)
            {
                Vector3 fighterPos = f.Value.transform.position;

                // Convert to viewport coordinates
                Vector3 vp = Cam.WorldToViewportPoint(fighterPos);

                //If fighter is behind the camera, flip the viewport point
                if (vp.z < 0) vp *= -1;

                //Clamp viewport to screen edges
                vp.x = Mathf.Clamp(vp.x, 0.05f, 0.95f);
                vp.y = Mathf.Clamp(vp.y, 0.05f, 0.95f);
                vp.z = Mathf.Abs(vp.z);

                //Convert back to world position for tracker
                Vector3 newPos = Cam.ViewportToWorldPoint(vp);

                //Rotate tracker toward fighter
                Vector3 toFighter = fighterPos - newPos;
                float angle = Mathf.Atan2(toFighter.y, toFighter.x) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(0f, 0f, angle - 90f);

                //Apply position and rotation
                GameObject t;
                if (!Trackers.ContainsKey(f.Key))
                {
                    t = Instantiate(TrackerPrefab, newPos, rot);
                    Trackers.Add(f.Key, t);
                }
                else
                {
                    t = Trackers[f.Key];
                    t.transform.position = newPos;
                    t.transform.rotation = rot;
                }

            }

            //Clearing out trackers for visible fighters
            List<int> toRemove = new List<int>();

            foreach (var kvp in Trackers)
            {
                if (!NotVisibleFighters.ContainsKey(kvp.Key))
                    toRemove.Add(kvp.Key);
            }

            foreach (int key in toRemove)
            {
                Destroy(Trackers[key]);
                Trackers.Remove(key);
            }
        }
    }
}
