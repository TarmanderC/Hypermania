using TMPro;
using UnityEngine;
using Utils;

namespace Game.View
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class RoundTimerView : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _roundTimer;
        int time;

        public void DisplayRoundTimer(Frame currentFrame, Frame roundEnd)
        {
            time = (roundEnd.No - currentFrame.No) / 60;
            gameObject.SetActive(time >= 0);
            _roundTimer.text = time.ToString();
        }

        void Start() { }

        // Update is called once per frame
        void Update()
        {
            _roundTimer.text = time.ToString();
        }
    }
}
