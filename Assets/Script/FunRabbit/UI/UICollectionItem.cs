using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UICollectionItem : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI titleName;
    [SerializeField] Image thumbnailIcon;

    // 진행 중인 썸네일 비동기 로드를 취소하기 위한 토큰 소스
    private CancellationTokenSource _loadCts;

    public void Set(string title, string fullPath)
    {
        if (titleName != null)
            titleName.text = title;

        // 이전에 진행 중이던 로드가 있으면 취소 (중복 호출 대비)
        CancelLoad();

        _loadCts = new CancellationTokenSource();

        // 썸네일은 비동기로 로드 (fire-and-forget)
        _ = LoadThumbnailAsync(fullPath, _loadCts.Token);
    }

    private async Awaitable LoadThumbnailAsync(string fullPath, CancellationToken token)
    {
        if (string.IsNullOrEmpty(fullPath))
            return;

        try
        {
            ResourceRequest request = Resources.LoadAsync<Sprite>(fullPath);

            // 로드가 끝날 때까지 프레임 단위로 대기 (취소 시 OperationCanceledException 발생)
            while (!request.isDone)
            {
                await Awaitable.NextFrameAsync(token);
            }

            token.ThrowIfCancellationRequested();

            if (request.asset is Sprite sprite)
            {
                if (thumbnailIcon != null)
                {
                    thumbnailIcon.sprite = sprite;
                    thumbnailIcon.SetNativeSize();
                }
            }
            else
            {
                Debug.LogError($"[UICollectionItem] 썸네일 로드 실패: {fullPath}");
            }
        }
        catch (OperationCanceledException)
        {
            // 오브젝트 파괴 등으로 비동기 로드가 취소됨 - 정상 흐름이므로 무시
        }
        catch (Exception e)
        {
            Debug.LogError($"[UICollectionItem] 썸네일 로드 중 예외: {fullPath}\n{e}");
        }
    }

    private void CancelLoad()
    {
        if (_loadCts != null)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }
    }

    private void OnDestroy()
    {
        // 파괴 시 진행 중인 비동기 로드를 취소하여 예외/콜백을 안전하게 정리
        CancelLoad();
    }
}
