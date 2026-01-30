using System;
using System.Collections.Generic;
using Design;
using UnityEngine;

namespace Game.View
{
    public class CameraControl : MonoBehaviour
    {
        private Camera Camera;

        [SerializeField]
        private float CameraSpeed = 10;

        [SerializeField]
        private GlobalConfig Config;

        // Additional area outside the arena bounds that the camera is allowed to see
        [SerializeField]
        private float Margin;

        void Start()
        {
            Camera = GetComponent<Camera>();
        }

        public void OnValidate()
        {
            if (Config == null)
            {
                throw new InvalidOperationException(
                    "Must set the config field on CameraControl because it reference the arena bounds"
                );
            }
        }

        public void UpdateCamera(List<Vector2> interestPoints, float zoom, float time)
        {
            Vector2 center = CalculateCenter(interestPoints);
            //Interpolation to center
            Vector2 pos = Vector2.Lerp(transform.position, center, CameraSpeed * time);
            transform.position = new Vector3(pos.x, pos.y, transform.position.z);
            //Interpolation to zoom
            Camera.orthographicSize = Mathf.Lerp(Camera.orthographicSize, zoom, CameraSpeed * time);
        }

        // Recalculates the center of interestPoints
        Vector2 CalculateCenter(List<Vector2> interestPoints)
        {
            if (interestPoints.Count == 0)
                return Vector2.zero;

            Vector2 NewCenter = Vector2.zero;
            foreach (Vector2 point in interestPoints)
                NewCenter += point;

            NewCenter /= interestPoints.Count;
            // Calculating visual area given aspect and zoom
            float halfHeight = Camera.orthographicSize;
            float halfWidth = Camera.orthographicSize * Camera.aspect;

            float minX = (float)-Config.WallsX + halfWidth - Margin;
            float maxX = (float)Config.WallsX - halfWidth + Margin;
            float minY = (float)Config.GroundY + halfHeight - Margin;
            float maxY = float.PositiveInfinity;

            // Clamping Camera View
            if (minX > maxX || minY > maxY)
            {
                throw new InvalidOperationException("bounds too small");
            }

            NewCenter.x = Mathf.Clamp(NewCenter.x, minX, maxX);
            NewCenter.y = Mathf.Clamp(NewCenter.y, minY, maxY);

            return new Vector2(NewCenter.x, NewCenter.y);
        }
    }
}
