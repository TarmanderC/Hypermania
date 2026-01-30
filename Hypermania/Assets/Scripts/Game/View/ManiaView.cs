using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UI;
using Utils;

namespace Game.View
{
    [Serializable]
    public struct ManiaViewConfig
    {
        public float ScrollSpeed;
        public Transform[] Anchors;
        public GameObject[] Notes;

        public void Validate()
        {
            if (ScrollSpeed == 0f)
            {
                throw new InvalidOperationException("Scroll speed in ManiaViewConfig cannot be 0");
            }
            if (Anchors == null || Anchors.Length == 0)
            {
                throw new InvalidOperationException("Must set note anchors");
            }
            if (Notes == null || Notes.Length == 0)
            {
                throw new InvalidOperationException("Must set note prefabs");
            }
        }
    }

    public class ManiaView : MonoBehaviour
    {
        [SerializeField]
        public ManiaViewConfig Config;
        private Dictionary<int, GameObject> _activeNotes;

        public void Init()
        {
            _activeNotes = new Dictionary<int, GameObject>();
            gameObject.SetActive(false);
        }

        public void DeInit()
        {
            foreach (var obj in _activeNotes.Values)
            {
                Destroy(obj);
            }
            _activeNotes = null;
        }

        public void OnValidate()
        {
            Config.Validate();
        }

        public void Render(Frame frame, in ManiaState state)
        {
            gameObject.SetActive(state.EndFrame != Frame.NullFrame);

            Dictionary<int, GameObject> renderedNow = new Dictionary<int, GameObject>();

            for (int i = 0; i < state.Config.NumKeys; i++)
            {
                for (int j = 0; j < state.Channels[i].Notes.Count; j++)
                {
                    if (!RenderNote(frame, i, state.Channels[i].Notes[j], out var noteView))
                    {
                        break;
                    }
                    renderedNow[state.Channels[i].Notes[j].Id] = noteView;
                }
            }

            // for all notes not rendered now but in the active set, destroy
            foreach ((var id, var obj) in _activeNotes)
            {
                if (!renderedNow.ContainsKey(id))
                {
                    Destroy(obj);
                }
            }
            _activeNotes = renderedNow;
        }

        private bool RenderNote(Frame frame, int channel, in ManiaNote note, out GameObject noteView)
        {
            noteView = null;
            float x = Config.Anchors[channel].localPosition.x;
            float y = (note.Tick - frame) * Config.ScrollSpeed + Config.Anchors[channel].localPosition.y;
            // dont render nodes that are way too far outside
            if (y > GetComponent<RectTransform>().rect.height / 2)
            {
                return false;
            }

            if (!_activeNotes.ContainsKey(note.Id))
            {
                noteView = Instantiate(Config.Notes[channel]);
                noteView.transform.SetParent(transform);
            }
            else
            {
                noteView = _activeNotes[note.Id];
            }

            noteView.transform.SetLocalPositionAndRotation(new Vector3(x, y, -1), Quaternion.identity);
            return true;
        }
    }
}
