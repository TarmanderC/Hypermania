using System;
using System.Collections.Generic;
using Design;
using Game.View;
using TMPro;
using UnityEngine;

public class NC_InfoOverlayView : MonoBehaviour
{
    public void Render(NC_InfoOverlayDetails details)
    {
        string detailsString = "FPS: " + details.FPS;
        if (details.HasPing)
        {
            detailsString += details.PingMs + "ms";
        }
        GetComponent<TMP_Text>().SetText(detailsString);
    }
}

public struct NC_InfoOverlayDetails
{
    public int FPS;
    public bool HasPing;
    public ulong PingMs;
}
