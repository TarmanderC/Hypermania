using System.Collections.Generic;
using UnityEngine;

public class DJ_CameraControl : MonoBehaviour
{
    private Camera Camera;

    [SerializeField]
    private float CameraSpeed = 10;

    [SerializeField]
    private Vector2 XBounds = new Vector2(0, 20);

    [SerializeField]
    private Vector2 YBounds = new Vector2(-10, 10);
    private Vector3 Center = new Vector3(0, 0, 0);
    private List<Vector2> InterestPoints = new List<Vector2>();

    void Start()
    {
        Camera = this.GetComponent<Camera>();
    }

    public void UpdateCamera(List<Vector2> interestPoints, float zoom, float time)
    {
        UpdateInterestPoints(interestPoints);
        UpdateCenter();
        //Interpolation to center
        transform.position = Vector3.Lerp(transform.position, Center, CameraSpeed * time);
        //Interpolation to zoom
        Camera.orthographicSize = Mathf.Lerp(Camera.orthographicSize, zoom, CameraSpeed * time);
    }

    // Recalculates the center of interestPoints
    void UpdateCenter()
    {
        if (InterestPoints.Count == 0)
            return;

        Vector2 NewCenter = Vector2.zero;
        foreach (Vector2 point in InterestPoints)
            NewCenter += point;

        NewCenter /= InterestPoints.Count;
        // Calculating visual area given aspect and zoom
        float halfHeight = Camera.orthographicSize;
        float halfWidth = Camera.orthographicSize * Camera.aspect;

        float minX = XBounds.x + halfWidth;
        float maxX = XBounds.y - halfWidth;
        float minY = YBounds.x + halfHeight;
        float maxY = YBounds.y - halfHeight;

        // Clamping Camera View
        if (minX > maxX)
            NewCenter.x = (XBounds.x + XBounds.y) * 0.5f;
        else
            NewCenter.x = Mathf.Clamp(NewCenter.x, minX, maxX);

        if (minY > maxY)
            NewCenter.y = (YBounds.x + YBounds.y) * 0.5f;
        else
            NewCenter.y = Mathf.Clamp(NewCenter.y, minY, maxY);

        Center = new Vector3(NewCenter.x, NewCenter.y, transform.position.z);
    }

    private void UpdateInterestPoints(List<Vector2> Points)
    {
        InterestPoints = Points;
    }
    public Vector2 GetXBounds()
    {
        return XBounds;
    }
    public Vector2 GetYBounds()
    {
        return YBounds;
    }
    public void SetXBounds(Vector2 newBounds)
    {
        XBounds = newBounds;
    }
    public void SetYBounds(Vector2 newBounds)
    {
        YBounds = newBounds;
    }
}
