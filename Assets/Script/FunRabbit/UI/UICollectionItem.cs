using System;
using System.Threading;
using FunRabbit;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UICollectionItem : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] TextMeshProUGUI titleName;
    [SerializeField] Image thumbnailIcon;

    // 진행 중인 썸네일 비동기 로드를 취소하기 위한 토큰 소스
    private CancellationTokenSource _loadCts;

    // 선택(클릭) 시 사용할 데이터
    private string _title;
    private string _modelFullPath;

    // active일 때 alpha 255(1.0), 비활성일 때 alpha 120(약 0.47)
    private const float ActiveAlpha = 255f / 255f;
    private const float InactiveAlpha = 120f / 255f;

    private void SetActiveItem(bool active)
    {
        float alpha = active ? ActiveAlpha : InactiveAlpha;

        if (titleName != null)
        {
            Color c = titleName.color;
            c.a = alpha;
            titleName.color = c;
        }

        // 미획득(미클리어) 인형은 보스 버전 썸네일({animalKey}_boss)이 대신 표시되므로
        // 검은 실루엣 처리 없이 알파만 낮춰 미획득 상태를 표현한다.
        if (thumbnailIcon != null)
        {
            Color c = Color.white;
            c.a = alpha;
            thumbnailIcon.color = c;
        }
    }

    public void Set(string title, string fullPath, string modelFullPath, bool active)
    {
        _title = title;
        _modelFullPath = modelFullPath;

        if (titleName != null)
            titleName.text = title;

        SetActiveItem(active);

        // 로드 전에는 무조건 숨겨서 이전 썸네일이 잠깐 보이는 것을 방지
        if (thumbnailIcon != null)
            thumbnailIcon.gameObject.SetActive(false);

        // 이전에 진행 중이던 로드가 있으면 취소 (중복 호출 대비)
        CancelLoad();

        _loadCts = new CancellationTokenSource();

        // 썸네일은 비동기로 로드 (fire-and-forget)
        _ = LoadThumbnailAsync(fullPath, _loadCts.Token);
    }

    // 클릭 시: 상세 팝업을 열고 모델을 로드한다. (미클리어 아이템은 보스 모델 경로가 전달돼 있음)
    public void OnPointerClick(PointerEventData eventData)
    {
        if (string.IsNullOrEmpty(_modelFullPath))
        {
            Debug.LogWarning($"[UICollectionItem] 모델 경로가 비어있습니다: {_title}");
            return;
        }

        var popup = UICollectionDetailPopup.CreateOrGet();
        if (popup != null)
            popup.SetData(_title, _modelFullPath);
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
                    thumbnailIcon.gameObject.SetActive(true);
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
