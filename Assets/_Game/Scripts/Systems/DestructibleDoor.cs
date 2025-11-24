using UnityEngine;
using System.Collections.Generic;
using LightPath.Utils; 

namespace LightPath.Systems
{
    public class DestructibleDoor : MonoBehaviour
    {
        [Header("데이터 연결")]
        public DoorProfile profile; // [핵심] 여기만 연결하면 됨

        [Header("인스턴스 상태 (Read Only)")]
        [SerializeField] private float currentHP;
        [SerializeField] private float currentStability;

        [Header("비주얼 (프리팹 내부 참조)")]
        public Transform visualSlabRoot; 
        public GameObject normalMesh;    
        
        [System.Serializable]
        public struct DamageVisual
        {
            public float hpThreshold;
            public GameObject meshObject; 
        }
        public List<DamageVisual> damageStages; 

        [Header("애니메이션")]
        public Animator doorAnimator; 

        private Collider doorCollider;
        private bool isBroken = false;
        private bool isOpen = false;

        private void Start()
        {
            // 비주얼 단계 정렬
            damageStages.Sort((a, b) => a.hpThreshold.CompareTo(b.hpThreshold));
            InitializeDoor();
        }

        public void InitializeDoor()
        {
            if (profile == null)
            {
                Debug.LogError($"Door Profile is missing on {gameObject.name}!");
                return;
            }

            // 프로필에서 스탯 가져오기
            currentHP = profile.maxHP;
            currentStability = profile.maxStability;
            
            isBroken = false;
            isOpen = false;
            
            doorCollider = GetComponent<Collider>();
            if (doorCollider) doorCollider.enabled = true;

            UpdateVisuals();
        }

        public void TakeHit(float damage, float impact, Vector3 attackerPos)
        {
            if (isBroken || isOpen || profile == null) return;

            // 1. 방향 판별 & 비주얼 반전
            Vector3 dirToAttacker = (attackerPos - transform.position).normalized;
            bool isHitFromFront = Vector3.Dot(transform.forward, dirToAttacker) > 0;

            if (visualSlabRoot)
            {
                float zScale = isHitFromFront ? 1f : -1f;
                visualSlabRoot.localScale = new Vector3(1, 1, zScale);
            }

            // 2. 데미지 적용
            currentHP -= damage;
            
            if (!profile.ignoreForceOpen)
            {
                currentStability -= impact;
                if (doorAnimator) doorAnimator.SetTrigger("Shake"); 
            }

            // 3. 이펙트 (프로필에서 가져옴)
            if (profile.hitFX) 
                Instantiate(profile.hitFX, transform.position, Quaternion.LookRotation(-dirToAttacker));

            // 4. 결과 판정
            if (currentHP <= 0)
            {
                BreakDoor(-dirToAttacker, impact * 10f);
            }
            else if (!profile.ignoreForceOpen && currentStability <= 0)
            {
                ForceOpen(isHitFromFront);
            }
            else
            {
                UpdateVisuals();
            }
        }

        void ForceOpen(bool isHitFromFront)
        {
            isOpen = true;
            if (doorAnimator)
            {
                string triggerName = isHitFromFront ? "OpenIn" : "OpenOut";
                doorAnimator.SetTrigger(triggerName);
            }
            if (doorCollider) doorCollider.isTrigger = true;
        }

        void UpdateVisuals()
        {
            if (normalMesh) normalMesh.SetActive(false);
            foreach (var stage in damageStages) if (stage.meshObject) stage.meshObject.SetActive(false);
            
            // 프로필에 있는 흔적 프리팹들은 여기서 제어 안 함 (Instantiate 방식이므로)

            bool foundStage = false;
            foreach (var stage in damageStages)
            {
                if (currentHP <= stage.hpThreshold)
                {
                    if (stage.meshObject) stage.meshObject.SetActive(true);
                    foundStage = true;
                    break; 
                }
            }

            if (!foundStage && normalMesh) normalMesh.SetActive(true);
        }

        void BreakDoor(Vector3 hitDir, float force)
        {
            isBroken = true;
            if (doorCollider) doorCollider.enabled = false;
            if (visualSlabRoot) visualSlabRoot.gameObject.SetActive(false);

            // [프로필 사용] 파괴 흔적 생성
            if (profile.destroyedTraces != null && profile.destroyedTraces.Count > 0)
            {
                GameObject tracePrefab = profile.destroyedTraces[CoreRandom.Range(0, profile.destroyedTraces.Count)];
                if (tracePrefab) 
                {
                    // 흔적은 자식으로 생성해서 위치 고정
                    Instantiate(tracePrefab, transform.position, transform.rotation, transform.parent);
                }
            }

            // [프로필 사용] 물리 파편 생성
            if (profile.physicsDebrisPrefab)
            {
                GameObject debris = Instantiate(profile.physicsDebrisPrefab, transform.position, transform.rotation);
                
                Rigidbody[] rbs = debris.GetComponentsInChildren<Rigidbody>();
                foreach (var rb in rbs)
                {
                    rb.AddForce(hitDir * force, ForceMode.Impulse);
                }
                Destroy(debris, profile.debrisLifetime);
            }
        }
    }
}