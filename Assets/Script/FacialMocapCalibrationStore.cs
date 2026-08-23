using UnityEngine;

public readonly struct FacialMocapCalibrationSettings
{
    public FacialMocapCalibrationSettings(
        Vector3 calibrationOffsetEuler,
        Vector3 rotationMultiplier,
        float headSensitivity,
        Vector3 additionalOffset)
    {
        CalibrationOffsetEuler = calibrationOffsetEuler;
        RotationMultiplier = rotationMultiplier;
        HeadSensitivity = headSensitivity;
        AdditionalOffset = additionalOffset;
    }

    public Vector3 CalibrationOffsetEuler { get; }
    public Vector3 RotationMultiplier { get; }
    public float HeadSensitivity { get; }
    public Vector3 AdditionalOffset { get; }
}

public static class FacialMocapCalibrationStore
{
    private const string StoragePrefix = "ProjectVirtual.iFacialMocap.Calibration.";

    private static readonly string[] RequiredKeys =
    {
        "offset.x", "offset.y", "offset.z",
        "multiplier.x", "multiplier.y", "multiplier.z",
        "headSensitivity",
        "additionalOffset.x", "additionalOffset.y", "additionalOffset.z"
    };

    public static void Save(string profileName, FacialMocapCalibrationSettings settings)
    {
        string storageKey = GetStorageKey(profileName);

        PlayerPrefs.SetFloat(storageKey + "offset.x", settings.CalibrationOffsetEuler.x);
        PlayerPrefs.SetFloat(storageKey + "offset.y", settings.CalibrationOffsetEuler.y);
        PlayerPrefs.SetFloat(storageKey + "offset.z", settings.CalibrationOffsetEuler.z);
        PlayerPrefs.SetFloat(storageKey + "multiplier.x", settings.RotationMultiplier.x);
        PlayerPrefs.SetFloat(storageKey + "multiplier.y", settings.RotationMultiplier.y);
        PlayerPrefs.SetFloat(storageKey + "multiplier.z", settings.RotationMultiplier.z);
        PlayerPrefs.SetFloat(storageKey + "headSensitivity", settings.HeadSensitivity);
        PlayerPrefs.SetFloat(storageKey + "additionalOffset.x", settings.AdditionalOffset.x);
        PlayerPrefs.SetFloat(storageKey + "additionalOffset.y", settings.AdditionalOffset.y);
        PlayerPrefs.SetFloat(storageKey + "additionalOffset.z", settings.AdditionalOffset.z);
        PlayerPrefs.Save();
    }

    public static bool TryLoad(string profileName, out FacialMocapCalibrationSettings settings)
    {
        string storageKey = GetStorageKey(profileName);
        foreach (string key in RequiredKeys)
        {
            if (!PlayerPrefs.HasKey(storageKey + key))
            {
                settings = default;
                return false;
            }
        }

        settings = new FacialMocapCalibrationSettings(
            new Vector3(
                PlayerPrefs.GetFloat(storageKey + "offset.x"),
                PlayerPrefs.GetFloat(storageKey + "offset.y"),
                PlayerPrefs.GetFloat(storageKey + "offset.z")),
            new Vector3(
                PlayerPrefs.GetFloat(storageKey + "multiplier.x"),
                PlayerPrefs.GetFloat(storageKey + "multiplier.y"),
                PlayerPrefs.GetFloat(storageKey + "multiplier.z")),
            PlayerPrefs.GetFloat(storageKey + "headSensitivity"),
            new Vector3(
                PlayerPrefs.GetFloat(storageKey + "additionalOffset.x"),
                PlayerPrefs.GetFloat(storageKey + "additionalOffset.y"),
                PlayerPrefs.GetFloat(storageKey + "additionalOffset.z")));
        return true;
    }

    private static string GetStorageKey(string profileName)
    {
        return StoragePrefix + profileName + ".";
    }
}
