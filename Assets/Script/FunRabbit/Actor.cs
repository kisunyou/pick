using UnityEngine;

namespace FunRabbit
{
    public class Actor : MonoBehaviour
    {
        private void Start()
        {
            StageManager.AddActor(this);
        }

        private void OnDestroy()
        {
            StageManager.RemoveActor(this);
        }
    }
}
