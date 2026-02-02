using TMPro;
using UnityEngine;

namespace Game.View
{
    public class InfoOverlayView : MonoBehaviour
    {
        private float _lastRenderTs = float.NaN;
        private float _accum = 0;
        private int _ticks = 0;
        private int _fps = 0;

        public void Render(InfoOverlayDetails details)
        {
            float now = Time.realtimeSinceStartup;

            _ticks++;
            if (!double.IsNaN(_lastRenderTs))
            {
                _accum += now - _lastRenderTs;
                if (_accum >= 1f)
                {
                    _fps = Mathf.RoundToInt(_ticks / _accum);
                    _accum = 0f;
                    _ticks = 0;
                }
            }

            _lastRenderTs = now;

            string detailsString = "FPS: " + _fps;
            if (details.HasPing)
            {
                detailsString += "  Ping: " + details.Ping + "ms";
            }
            GetComponent<TMP_Text>().SetText(detailsString);
        }
    }

    public struct InfoOverlayDetails
    {
        public bool HasPing;
        public ulong Ping;
    }
}
