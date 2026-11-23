using UnityEngine;

public class QtxQualityChoice : ScriptableObject
{
    [SerializeField] private int _quality;

    public int Quality => _quality;

    public static QtxQualityChoice Create(int quality)
    {
        var choice = CreateInstance<QtxQualityChoice>();
        choice._quality = quality;

        return choice;
    }
}
