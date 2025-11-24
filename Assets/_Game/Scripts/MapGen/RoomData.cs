using UnityEngine;
using System.Collections.Generic;
using LightPath.Utils; 
using static LightPath.Utils.RoomGizmo; // RoomGizmo의 DoorInfo 구조체 사용

namespace LightPath.MapGen
{
    public enum RoomType { Normal, Start, Altar, Escape, Special }

    [CreateAssetMenu(fileName = "NewRoomData", menuName = "LightPath/Room Data")]
    public class RoomData : ScriptableObject
    {
        [Header("기본 정보")]
        public string id;
        public RoomType type;
        public GameObject prefab; 

        private RoomGizmo _cachedGizmo;
        private RoomGizmo Gizmo
        {
            get
            {
                if (_cachedGizmo == null && prefab != null)
                {
                    _cachedGizmo = prefab.GetComponent<RoomGizmo>();
                }
                return _cachedGizmo;
            }
        }

        // --- 자동 연동 프로퍼티 (RoomGizmo의 doors 리스트에서 직접 추출) ---
        
        public int width => (Gizmo != null) ? Gizmo.width : 4;
        public int height => (Gizmo != null) ? Gizmo.height : 4;

        public List<Vector2Int> possibleDoorSpots
        {
            get
            {
                List<Vector2Int> spots = new List<Vector2Int>();
                // [수정] Gizmo.doors에서 pos만 추출
                if (Gizmo != null && Gizmo.doors != null)
                {
                    foreach (var d in Gizmo.doors) spots.Add(d.pos);
                }
                return spots;
            }
        }

        public List<Vector2Int> doorDirections
        {
            get
            {
                List<Vector2Int> dirs = new List<Vector2Int>();
                if (Gizmo != null && Gizmo.doors != null)
                {
                    foreach (var d in Gizmo.doors) dirs.Add(d.dir);
                }
                return dirs;
            }
        }

        public List<bool> canBeSliding
        {
            get
            {
                List<bool> slidings = new List<bool>();
                // [수정] Gizmo.doors에서 canBeSliding만 추출
                if (Gizmo != null && Gizmo.doors != null)
                {
                    foreach (var d in Gizmo.doors) slidings.Add(d.canBeSliding);
                }
                return slidings;
            }
        }
        
        public Vector3 GetSizeWorld()
        {
            float unit = (Gizmo != null) ? Gizmo.unitSize : 3f;
            return new Vector3(width * unit, 4f, height * unit);
        }
    }
}