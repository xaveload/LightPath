using UnityEngine;
using LightPath.Utils;
using LightPath.Systems;

// 디버깅용 스크립트
public class SeedTester : MonoBehaviour
{
    public int tryCount = 10;   
    public ItemSpawner spawner; 

    void Start()
    {
        // 주의: 시드는 GameManager가 이미 초기화했습니다.
        Debug.Log($"--- 아이템 테스트 시작 (현재 시드: {CoreRandom.CurrentSeed}) ---");

        spawner.InitializeBag();

        for (int i = 0; i < tryCount; i++)
        {
            ItemData result = spawner.TrySpawnItem();
            string logMsg = result == null ? "꽝" : result.itemName;
            Debug.Log($"[{i + 1}번째 시도] 결과: {logMsg}");
        }
        Debug.Log("--- 테스트 종료 ---");
    }
}