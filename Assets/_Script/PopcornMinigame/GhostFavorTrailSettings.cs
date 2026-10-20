using System;
using UnityEngine;

[Serializable]
public sealed class GhostFavorTrailSettings
{
    public bool enabled = true;
    [Header("Navigation")]
    public int agentTypeId;
    public int areaMask = -1;
    [Min(.1f)] public float navMeshSampleRadius = 1.5f;
    [Tooltip("Maximum distance from the walkable endpoint to the item. Must fit the existing pickup range.")]
    [Range(.5f, 3f)] public float pickupReach = 2f;
    [Range(.2f, 2.5f)] public float maximumFloorHeightDifference = 1.8f;
    [Min(.5f)] public float blockedPathCheckInterval = 2f;
    [Min(.1f)] public float settleSeconds = .6f;
    [Min(.01f)] public float movementThreshold = .08f;
    [Header("Floor placement")]
    public LayerMask groundLayers = ~0;
    [Range(.1f, 1f)] public float groundProbeHeight = .4f;
    [Range(.1f, 1f)] public float groundProbeDepth = .5f;
    [Range(.01f, .3f)] public float maximumSurfaceError = .2f;
    [Range(0f, .05f)] public float surfaceOffset = .012f;
    [Header("Blood marks")]
    [Min(.3f)] public float spacing = 1.2f;
    public Vector2 markSize = new Vector2(.35f, .65f);
    [Range(0f, .2f)] public float sidewaysVariation = .07f;
    [Range(8, 256)] public int maximumMarks = 128;
    [Range(8, 48)] public int marksPerMesh = 24;
    public Color bloodColor = new Color(.3f, .012f, .025f, .9f);
    [Tooltip("Optional blood PNG with transparency. Empty uses the shader's procedural drops and drag marks.")]
    public Texture2D bloodTexture;
    [Range(0f, 1f)] public float minimumVisibility = .2f;
    [Header("Automatic light sweep (no aiming required)")]
    public Color sweepColor = new Color(1f, .2f, .12f, 1f);
    [Range(0f, 5f)] public float sweepIntensity = 1.4f;
    [Min(.2f)] public float sweepDuration = 1.5f;
    [Min(.1f)] public float sweepPause = 9f;
    [Min(.1f)] public float sweepWidth = 1.8f;
    [Header("Visibility and pooling")]
    [Min(2f)] public float visibleDistance = 28f;
    [Min(.1f)] public float distanceFade = 5f;
    [Min(.05f)] public float fadeSeconds = 1.2f;
}

/// <summary>One small snapshot per route change; never a network object per blood mark.</summary>
[Serializable]
public struct GhostFavorTrailSnapshot
{
    public uint revision;
    public bool active;
    public int seed;
    public double pulseEpoch;
    public Vector3[] corners;
}
