using UnityEngine;

namespace TankGame.Prototype
{
    [CreateAssetMenu(menuName = "Tank/Map", fileName = "MapDef")]
    public sealed class MapDef : ScriptableObject
    {
        public string displayName = "Map";
        public GameObject prefab;       // contains a MapLayout
    }
}
