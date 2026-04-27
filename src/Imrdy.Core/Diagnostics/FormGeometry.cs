namespace Imrdy.Core.Diagnostics;

/// <summary>
/// Screen and client geometry of the inspected form.
/// </summary>
public record FormGeometry(
    int FormX,
    int FormY,
    int FormWidth,
    int FormHeight,
    int ClientWidth,
    int ClientHeight,
    int RegionRadius);
