using System.Collections.Generic;
using UnityEngine;

namespace Tank.Gameplay.Fx
{
    /// <summary>Reuses instances of prefabs: effects and projectiles come and go constantly, so none of them is created or destroyed during play.</summary>
    public sealed class ObjectPool
    {
        readonly Transform m_Root;
        readonly Dictionary<GameObject, Stack<GameObject>> m_Free = new Dictionary<GameObject, Stack<GameObject>>();
        readonly Dictionary<GameObject, GameObject> m_PrefabOf = new Dictionary<GameObject, GameObject>();

        public ObjectPool(Transform root) { m_Root = root; }

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            if (!m_Free.TryGetValue(prefab, out Stack<GameObject> stack)) m_Free[prefab] = stack = new Stack<GameObject>();
            GameObject go = null;
            while (stack.Count > 0 && go == null) go = stack.Pop();
            if (go == null) { go = Object.Instantiate(prefab, m_Root); m_PrefabOf[go] = prefab; }
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            return go;
        }

        public void Release(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            if (m_PrefabOf.TryGetValue(instance, out GameObject prefab)) m_Free[prefab].Push(instance); else Object.Destroy(instance);
        }
    }
}
