using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 스프라이트 Resources.Load 캐시. UI2/Thumbnail 폴더는 게임 시작 시 Preload로 한꺼번에 읽어두고,
    // 그 외 경로는 Get 호출 시점에 처음 로드해 캐시한다 (동일 경로 반복 Resources.Load 방지).
    public static class SpriteCache
    {
        private const string ThumbnailFolderPath = "UI2/Thumbnail";

        // Resources 경로 -> 로드된 스프라이트 (실패한 경로도 null로 캐싱해 재시도하지 않는다)
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        // UI2/Thumbnail 폴더의 모든 스프라이트를 미리 로드해 캐시를 채운다. (게임 시작 시 1회 호출)
        public static void Preload()
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(ThumbnailFolderPath);
            foreach (Sprite sprite in sprites)
                _cache[$"{ThumbnailFolderPath}/{sprite.name}"] = sprite;

            Debug.Log($"[SpriteCache] {ThumbnailFolderPath} 프리로드 완료: {sprites.Length}개");
        }

        // 캐시에 있으면 그대로 반환하고, 없으면 Resources.Load로 읽어 캐시에 채운 뒤 반환한다.
        public static Sprite Get(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (_cache.TryGetValue(path, out Sprite cached))
                return cached;

            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
                Debug.LogError($"[SpriteCache] 스프라이트 로드 실패: {path}");

            _cache[path] = sprite;
            return sprite;
        }
    }
}
