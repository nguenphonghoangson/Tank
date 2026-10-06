using UnityEngine;
using UnityEngine.InputSystem;

namespace TankGame.Prototype
{
    /// <summary>Prototype loop only: respawn the dummy enemy, respawn the player, count kills.</summary>
    public sealed class PrototypeGame : MonoBehaviour
    {
        public TankUnit player;
        public TankUnit enemy;
        public Transform playerSpawn;
        public Transform[] enemySpawns;
        public float enemyRespawnDelay = 1.8f;
        public float playerRespawnDelay = 2.5f;

        public int Kills { get; private set; }

        int m_SpawnIndex;
        float m_EnemyRespawnAt = -1f, m_PlayerRespawnAt = -1f, m_DamageFlash;

        /// <summary>0..1, decays quickly after the player takes a hit. Read by the debug HUD.</summary>
        public float DamageFlash => m_DamageFlash;

        void OnEnable()
        {
            player.Damaged += OnPlayerDamaged;
            player.Died += OnPlayerDied;
            enemy.Died += OnEnemyDied;
        }

        void OnDisable()
        {
            player.Damaged -= OnPlayerDamaged;
            player.Died -= OnPlayerDied;
            enemy.Died -= OnEnemyDied;
        }

        void Start()
        {
            RespawnEnemy();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (m_EnemyRespawnAt >= 0f && now >= m_EnemyRespawnAt) RespawnEnemy();
            if (m_PlayerRespawnAt >= 0f && now >= m_PlayerRespawnAt) RespawnPlayer();
            if (m_DamageFlash > 0f) m_DamageFlash -= Time.unscaledDeltaTime * 2.5f;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) ResetAll();
        }

        void OnEnemyDied(TankUnit u)
        {
            Kills++;
            m_EnemyRespawnAt = Time.unscaledTime + enemyRespawnDelay;
        }

        void OnPlayerDied(TankUnit u) { m_PlayerRespawnAt = Time.unscaledTime + playerRespawnDelay; }
        void OnPlayerDamaged(TankUnit u, int damage, Vector3 point, Vector3 dir) { m_DamageFlash = 1f; }

        public void RespawnEnemy()
        {
            Transform sp = enemySpawns[m_SpawnIndex++ % enemySpawns.Length];
            enemy.Respawn(sp.position, sp.rotation);
            m_EnemyRespawnAt = -1f;
        }

        public void RespawnPlayer()
        {
            player.Respawn(playerSpawn.position, playerSpawn.rotation);
            m_PlayerRespawnAt = -1f;
        }

        public void ResetAll()
        {
            Kills = 0;
            RespawnPlayer();
            RespawnEnemy();
        }
    }
}
