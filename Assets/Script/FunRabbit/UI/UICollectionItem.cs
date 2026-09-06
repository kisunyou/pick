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
    private string _animalKey;   // 상세 팝업 모델에 적용할 actor.json 행 키 (변형 등급 반영, 예: bear_g)

    // active일 때 alpha 255(1.0), 비활성일 때 alpha 120(약 0.47)
    private const float ActiveAlpha = 255f / 255f;
    private const float InactiveAlpha = 120f / 255f;

    // 변형(_g/_r) 클리어 등급 이름 색: _g 클리어 = 초록, _r 클리어 = 빨강 (등급 0은 프리팹 기본색 유지)
    private static readonly Color GradeGreenColor = new Color(0.30f, 0.85f, 0.35f);
    private static readonly Color GradeRedColor = new Color(0.95f, 0.30f, 0.25f);

    // 프리팹 기본 이름 색 (등급 0으로 되돌릴 때 사용) - 최초 Set 시점에 캐시
    private Color _titleBaseColor = Color.white;
    private bool _titleBaseColorCached;

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

    public void Set(UICollectionItemData data)
    {
        _title = data.title;
        _modelFullPath = data.modelPath;
        _animalKey = data.animalKey;

        if (titleName != null)
        {
            if (!_titleBaseColorCached)
            {
                _titleBaseColor = titleName.color;
                _titleBaseColorCached = true;
            }

            titleName.text = data.title;

            // 변형 클리어 등급을 이름 색으로 표현 (SetActiveItem이 뒤에서 알파만 덧씌운다)
            titleName.color = data.grade >= 2 ? GradeRedColor
                            : data.grade == 1 ? GradeGreenColor
                            : _titleBaseColor;
        }

        SetActiveItem(data.active);

        // 로드 전에는 무조건 숨겨서 이전 썸네일이 잠깐 보이는 것을 방지
        if (thumbnailIcon != null)
            thumbnailIcon.gameObject.SetActive(false);

        // 이전에 진행 중이던 로드가 있으면 취소 (중복 호출 대비)
        CancelLoad();

        _loadCts = new CancellationTokenSource();

        // 썸네일은 비동기로 로드 (fire-and-forget)
        _ = LoadThumbnailAsync(data.iconPath, _loadCts.Token);
    }

    // 클릭(터치 다운 후 제자리에서 업) 시: 상세 팝업을 열고 모델을 로드한다. (미클리어 아이템은 보스 모델 경로가 전달돼 있음)
    // 도감 리스트는 ScrollRect 안에 있으므로, 스크롤 제스처로 끝난 터치는 클릭으로 취급하지 않는다 -
    // 아니면 스크롤하려고 손을 대기만 해도 팝업이 떠서 스크롤이 불가능해진다.
    public void OnPointerClick(PointerEventData eventData)
    {
        // 드래그(스크롤) 중이었던 터치는 무시
        if (eventData.dragging)
            return;

        // 입력 모듈이 dragging을 세워주지 않는 환경 대비: 다운→업 이동 거리가
        // 드래그 판정 임계값을 넘으면 스크롤 의도로 보고 무시한다
        if (EventSystem.current != null)
        {
            float threshold = EventSystem.current.pixelDragThreshold;
            if ((eventData.position - eventData.pressPosition).sqrMagnitude > threshold * threshold)
                return;
        }

        if (string.IsNullOrEmpty(_modelFullPath))
        {
            Debug.LogWarning($"[UICollectionItem] 모델 경로가 비어있습니다: {_title}");
            return;
        }

        var popup = UICollectionDetailPopup.CreateOrGet();
        if (popup != null)
            popup.SetData(_title, _modelFullPath, _animalKey);
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
