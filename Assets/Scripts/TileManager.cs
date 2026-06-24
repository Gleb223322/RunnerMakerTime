using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TileManager:
/// - Управляет бесконечной дорогой (пул тайлов)
/// - Создаёт 3 дорожки (laneCount = 3) автоматически, без spawn points в префабах
/// - Спавнит препятствия через ObstacleSpawner внутри тайла (передаём абсолютные X-позиции дорожек)
/// - Спавнит монеты и бонусы (пул монет и пул бонусов)
/// - При рецикле тайла возвращает все монеты/бонусы/препятствия в соответствующие пулы
/// </summary>
public class TileManager : MonoBehaviour
{
    [Header("Tiles")]
    [SerializeField] List<GameObject> tilePrefabs = new List<GameObject>();
    [SerializeField] int initialTiles = 6;
    [SerializeField] float tileLength = 20f;
    [SerializeField] Transform player;

    [Header("Lanes")]
    [SerializeField] int laneCount = 3;               // оставляем 3, но можно настроить
    [SerializeField] float laneDistance = 2.5f;       // расстояние между дорожками (по X)

    [Header("Pooling")]
    [SerializeField] int poolPerTilePrefab = 3;

    [Header("Obstacles")]
    [SerializeField] float obstacleSpawnChancePerLane = 0.4f; // шанс препятствия в каждой дорожке

    [Header("Coins & Bonuses")]
    [SerializeField] GameObject coinPrefab;
    [SerializeField] GameObject magnetPrefab;
    [SerializeField] GameObject speedPrefab;
    [SerializeField] GameObject doubleScorePrefab;
    [SerializeField] float coinSpawnChance = 0.6f;    // вероятность генерации монеты в позиции
    [SerializeField] float bonusSpawnChance = 0.15f;  // шанс спавна бонуса на тайле
    [SerializeField] int coinPoolSize = 50;
    [SerializeField] int bonusPoolSizePerType = 5;
    [SerializeField] float coinSpacing = 1.0f;       // шаг по Z между монетами в "ряде"
    [SerializeField] float coinRowMinOffset = 2f;    // отступ от начала тайла
    [SerializeField] float coinRowMaxOffset = 16f;   // до границы тайла (зависит от tileLength)

    // runtime
    Dictionary<GameObject, Queue<GameObject>> tilePools = new Dictionary<GameObject, Queue<GameObject>>();
    Queue<GameObject> activeTiles = new Queue<GameObject>();

    Queue<GameObject> coinPool = new Queue<GameObject>();
    Dictionary<GameObject, Queue<GameObject>> bonusPools = new Dictionary<GameObject, Queue<GameObject>>();

    float spawnZ = 0f;
    float startX = 0f;

    public float TileLength => tileLength;
    public int LaneCount => laneCount;

    void Start()
    {
        if (player == null)
        {
            var pgo = GameObject.FindGameObjectWithTag("Player");
            if (pgo) player = pgo.transform;
        }

        startX = (player != null) ? player.position.x : 0f;

        // Create pools for tile prefabs
        foreach (var prefab in tilePrefabs)
        {
            var q = new Queue<GameObject>();
            for (int i = 0; i < poolPerTilePrefab; i++)
            {
                var go = Instantiate(prefab, Vector3.one * 10000f, Quaternion.identity, transform);
                go.SetActive(false);
                q.Enqueue(go);
            }
            tilePools[prefab] = q;
        }

        // create coin pool
        if (coinPrefab != null)
        {
            for (int i = 0; i < coinPoolSize; i++)
            {
                var c = Instantiate(coinPrefab, Vector3.one * 10000f, Quaternion.identity, transform);
                c.SetActive(false);
                var coinComp = c.GetComponent<Coin>();
                if (coinComp != null) coinComp.SetOriginPrefab(coinPrefab);
                coinPool.Enqueue(c);
            }
        }

        // bonus pools
        CreateBonusPool(magnetPrefab, bonusPoolSizePerType);
        CreateBonusPool(speedPrefab, bonusPoolSizePerType);
        CreateBonusPool(doubleScorePrefab, bonusPoolSizePerType);

        // spawn initial tiles
        for (int i = 0; i < initialTiles; i++)
        {
            SpawnTile(i == 0 ? tilePrefabs[0] : GetRandomTilePrefab());
        }
    }

    void CreateBonusPool(GameObject prefab, int size)
    {
        if (prefab == null) return;
        if (bonusPools.ContainsKey(prefab)) return;
        var q = new Queue<GameObject>();
        for (int i = 0; i < size; i++)
        {
            var go = Instantiate(prefab, Vector3.one * 10000f, Quaternion.identity, transform);
            go.SetActive(false);
            var bb = go.GetComponent<BonusBase>();
            if (bb != null) bb.SetOriginPrefab(prefab);
            q.Enqueue(go);
        }
        bonusPools[prefab] = q;
    }

    GameObject GetRandomTilePrefab()
    {
        if (tilePrefabs == null || tilePrefabs.Count == 0) return null;
        return tilePrefabs[Random.Range(0, tilePrefabs.Count)];
    }

    void SpawnTile(GameObject prefab)
    {
        if (prefab == null) return;
        GameObject tile;
        var pool = tilePools[prefab];
        if (pool.Count > 0)
        {
            tile = pool.Dequeue();
            tile.transform.SetParent(transform);
            tile.transform.position = Vector3.forward * spawnZ;
            tile.transform.rotation = Quaternion.identity;
            tile.SetActive(true);
        }
        else
        {
            tile = Instantiate(prefab, Vector3.forward * spawnZ, Quaternion.identity, transform);
        }

        // Compute lane local X positions relative to tile center.
        // For 3 lanes: indices 0,1,2 -> offsets -laneDistance, 0, +laneDistance
        float[] laneXs = new float[laneCount];
        float centerIndexOffset = (laneCount - 1) / 2f;
        for (int i = 0; i < laneCount; i++)
        {
            laneXs[i] = (i - centerIndexOffset) * laneDistance;
        }

        // Let ObstacleSpawner inside tile spawn obstacles using laneXs and tileLength
        var obstacleSpawner = tile.GetComponentInChildren<ObstacleSpawner>();
        if (obstacleSpawner != null)
        {
            obstacleSpawner.SpawnObstacles(tile.transform, laneXs, tileLength, obstacleSpawnChancePerLane);
        }

        // Spawn coins across lanes — several patterns are possible; сделаем простой: несколько рядов по Z
        SpawnCoinsOnTile(tile.transform, laneXs);

        // Spawn one bonus with some chance
        SpawnBonusOnTile(tile.transform, laneXs);

        activeTiles.Enqueue(tile);
        spawnZ += tileLength;
    }

    void SpawnCoinsOnTile(Transform tile, float[] laneXs)
    {
        if (coinPrefab == null || coinPool == null) return;

        // compute z range within tile (local)
        float zMin = coinRowMinOffset;
        float zMax = Mathf.Max(coinRowMinOffset + 1f, tileLength - 2f);
        for (float localZ = zMin; localZ <= zMax; localZ += coinSpacing)
        {
            // For each lane attempt to spawn a coin with coinSpawnChance
            for (int lane = 0; lane < laneXs.Length; lane++)
            {
                if (Random.value > coinSpawnChance) continue;

                GameObject coin = GetCoinFromPool();
                Vector3 localPos = new Vector3(laneXs[lane], 0.5f, localZ); // y=0.5f (пример)
                coin.transform.position = tile.TransformPoint(localPos);
                coin.transform.SetParent(tile, true); // дочерний к тайлу
                coin.SetActive(true);

                var coinComp = coin.GetComponent<Coin>();
                if (coinComp != null) coinComp.Init(player);
            }
        }
    }

    GameObject GetCoinFromPool()
    {
        if (coinPool.Count > 0)
        {
            var c = coinPool.Dequeue();
            return c;
        }
        else
        {
            var c = Instantiate(coinPrefab, Vector3.one * 10000f, Quaternion.identity, transform);
            var coinComp = c.GetComponent<Coin>();
            if (coinComp != null) coinComp.SetOriginPrefab(coinPrefab);
            return c;
        }
    }

    void SpawnBonusOnTile(Transform tile, float[] laneXs)
    {
        if (Random.value > bonusSpawnChance) return;
        int t = Random.Range(0, 3);
        GameObject prefab = t == 0 ? magnetPrefab : (t == 1 ? speedPrefab : doubleScorePrefab);
        if (prefab == null) return;

        GameObject bonus = GetBonusFromPool(prefab);
        int lane = Random.Range(0, laneXs.Length);
        float localZ = Random.Range(3f, tileLength - 3f);
        Vector3 localPos = new Vector3(laneXs[lane], 0.7f, localZ);
        bonus.transform.position = tile.TransformPoint(localPos);
        bonus.transform.rotation = tile.rotation;
        bonus.transform.SetParent(tile, true);
        bonus.SetActive(true);
    }

    GameObject GetBonusFromPool(GameObject prefab)
    {
        if (!bonusPools.ContainsKey(prefab))
        {
            CreateBonusPool(prefab, bonusPoolSizePerType);
        }
        var q = bonusPools[prefab];
        if (q.Count > 0)
        {
            var b = q.Dequeue();
            return b;
        }
        else
        {
            var b = Instantiate(prefab, Vector3.one * 10000f, Quaternion.identity, transform);
            var bb = b.GetComponent<BonusBase>();
            if (bb != null) bb.SetOriginPrefab(prefab);
            return b;
        }
    }

    void Update()
    {
        if (player == null || activeTiles.Count == 0) return;

        var first = activeTiles.Peek();
        if (player.position.z - first.transform.position.z > tileLength * 1.5f)
        {
            RecycleTile();
            SpawnTile(GetRandomTilePrefab());
        }
    }

    void RecycleTile()
    {
        if (activeTiles.Count == 0) return;
        var tile = activeTiles.Dequeue();

        var coinChildren = tile.GetComponentsInChildren<Coin>(true);
        foreach (var c in coinChildren)
        {
            ReturnCoinToPool(c.gameObject);
        }

        var bonusChildren = tile.GetComponentsInChildren<BonusBase>(true);
        foreach (var b in bonusChildren)
        {
            ReturnBonusToPool(b.OriginPrefab, b.gameObject);
        }

        var obstacleSpawner = tile.GetComponentInChildren<ObstacleSpawner>();
        if (obstacleSpawner != null)
        {
            obstacleSpawner.ReturnAllObstaclesInTile(tile);
        }
        else
        {
            var obstacleTransforms = tile.GetComponentsInChildren<Transform>(true);
            foreach (var tr in obstacleTransforms)
            {
                if (tr.gameObject.CompareTag("Obstacle"))
                {
                    tr.gameObject.SetActive(false);
                    tr.SetParent(transform);
                }
            }
        }

        tile.SetActive(false);

        GameObject originalPrefab = tile.GetComponent<Tile>()?.SourcePrefab;
        if (originalPrefab != null && tilePools.ContainsKey(originalPrefab))
        {
            tilePools[originalPrefab].Enqueue(tile);
        }
        else
        {
            Destroy(tile);
        }
    }

    void ReturnCoinToPool(GameObject coin)
    {
        if (coin == null) return;
        coin.SetActive(false);
        coin.transform.SetParent(transform, true);
        coinPool.Enqueue(coin);
    }

    void ReturnBonusToPool(GameObject prefab, GameObject instance)
    {
        if (instance == null) return;
        instance.SetActive(false);
        instance.transform.SetParent(transform, true);
        if (!bonusPools.ContainsKey(prefab))
        {
            bonusPools[prefab] = new Queue<GameObject>();
        }
        bonusPools[prefab].Enqueue(instance);
    }
}
