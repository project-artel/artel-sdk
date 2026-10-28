using global::UnityEngine;
using global::UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// 게임의 엔진 API 호출이 빌드 때 바뀌어 도착하는 자리. 원래 호출을 그대로 하고 <see cref="EngineEvents"/> 에 적는다.
    /// </summary>
    /// <remarks>
    /// <see cref="ArtelInput"/> 과 같은 방식이다. <c>EngineCallWeaver</c> 가 게임 어셈블리의 <c>AudioSource.Play()</c> 호출을
    /// <c>ArtelEngine.Play(source)</c> 로 바꾼다 — 인스턴스 메서드는 그 인스턴스가 첫 인자가 되고, static 은 그대로다.
    /// 메서드 이름과 인자 타입이 그대로여야 weaver 가 짝을 찾는다. 여기 없는 overload 는 바꾸지 않고 지나간다.
    ///
    /// 게임 코드가 직접 부르는 자리가 아니다.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class ArtelEngine
    {
        // --- AudioSource ------------------------------------------------------------------

        public static void Play(AudioSource source)
        {
            source.Play();
            EngineEvents.Record("audio.play", source, ClipName(source), null);
        }

        public static void Play(AudioSource source, ulong delay)
        {
            source.Play(delay);
            EngineEvents.Record("audio.play", source, ClipName(source), null);
        }

        public static void PlayDelayed(AudioSource source, float delay)
        {
            source.PlayDelayed(delay);
            EngineEvents.Record("audio.play", source, ClipName(source), null);
        }

        public static void PlayOneShot(AudioSource source, AudioClip clip)
        {
            source.PlayOneShot(clip);
            EngineEvents.Record("audio.play", source, clip == null ? null : clip.name, null);
        }

        public static void PlayOneShot(AudioSource source, AudioClip clip, float volumeScale)
        {
            source.PlayOneShot(clip, volumeScale);
            EngineEvents.Record("audio.play", source, clip == null ? null : clip.name, null);
        }

        public static void Stop(AudioSource source)
        {
            EngineEvents.Record("audio.stop", source, ClipName(source), null);
            source.Stop();
        }

        public static void PlayClipAtPoint(AudioClip clip, Vector3 position)
        {
            AudioSource.PlayClipAtPoint(clip, position);
            EngineEvents.Record("audio.play", null, clip == null ? null : clip.name, null);
        }

        public static void PlayClipAtPoint(AudioClip clip, Vector3 position, float volume)
        {
            AudioSource.PlayClipAtPoint(clip, position, volume);
            EngineEvents.Record("audio.play", null, clip == null ? null : clip.name, null);
        }

        private static string ClipName(AudioSource source)
        {
            return source != null && source.clip != null ? source.clip.name : null;
        }

        // --- Animator ---------------------------------------------------------------------

        public static void SetTrigger(Animator animator, string name)
        {
            animator.SetTrigger(name);
            EngineEvents.Record("anim.trigger", animator, name, null);
        }

        public static void SetTrigger(Animator animator, int id)
        {
            animator.SetTrigger(id);
            EngineEvents.Record("anim.trigger", animator, "#" + id, null);
        }

        public static void SetBool(Animator animator, string name, bool value)
        {
            animator.SetBool(name, value);
            EngineEvents.Record("anim.bool", animator, name, value);
        }

        public static void SetBool(Animator animator, int id, bool value)
        {
            animator.SetBool(id, value);
            EngineEvents.Record("anim.bool", animator, "#" + id, value);
        }

        public static void SetInteger(Animator animator, string name, int value)
        {
            animator.SetInteger(name, value);
            EngineEvents.Record("anim.int", animator, name, value);
        }

        public static void SetInteger(Animator animator, int id, int value)
        {
            animator.SetInteger(id, value);
            EngineEvents.Record("anim.int", animator, "#" + id, value);
        }

        public static void SetFloat(Animator animator, string name, float value)
        {
            animator.SetFloat(name, value);
            EngineEvents.Record("anim.float", animator, name, value);
        }

        public static void SetFloat(Animator animator, int id, float value)
        {
            animator.SetFloat(id, value);
            EngineEvents.Record("anim.float", animator, "#" + id, value);
        }

        public static void Play(Animator animator, string stateName)
        {
            animator.Play(stateName);
            EngineEvents.Record("anim.play", animator, stateName, null);
        }

        public static void Play(Animator animator, string stateName, int layer)
        {
            animator.Play(stateName, layer);
            EngineEvents.Record("anim.play", animator, stateName, null);
        }

        public static void Play(Animator animator, string stateName, int layer, float normalizedTime)
        {
            animator.Play(stateName, layer, normalizedTime);
            EngineEvents.Record("anim.play", animator, stateName, null);
        }

        public static void CrossFade(Animator animator, string stateName, float normalizedTransitionDuration)
        {
            animator.CrossFade(stateName, normalizedTransitionDuration);
            EngineEvents.Record("anim.play", animator, stateName, null);
        }

        public static void CrossFade(Animator animator, string stateName, float normalizedTransitionDuration, int layer)
        {
            animator.CrossFade(stateName, normalizedTransitionDuration, layer);
            EngineEvents.Record("anim.play", animator, stateName, null);
        }

        // --- Object -----------------------------------------------------------------------

        public static Object Instantiate(Object original)
        {
            var made = Object.Instantiate(original);
            Spawned(original, made);
            return made;
        }

        public static Object Instantiate(Object original, Transform parent)
        {
            var made = Object.Instantiate(original, parent);
            Spawned(original, made);
            return made;
        }

        public static Object Instantiate(Object original, Transform parent, bool instantiateInWorldSpace)
        {
            var made = Object.Instantiate(original, parent, instantiateInWorldSpace);
            Spawned(original, made);
            return made;
        }

        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation)
        {
            var made = Object.Instantiate(original, position, rotation);
            Spawned(original, made);
            return made;
        }

        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation, Transform parent)
        {
            var made = Object.Instantiate(original, position, rotation, parent);
            Spawned(original, made);
            return made;
        }

        public static T Instantiate<T>(T original) where T : Object
        {
            var made = Object.Instantiate(original);
            Spawned(original, made);
            return made;
        }

        public static T Instantiate<T>(T original, Transform parent) where T : Object
        {
            var made = Object.Instantiate(original, parent);
            Spawned(original, made);
            return made;
        }

        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object
        {
            var made = Object.Instantiate(original, parent, worldPositionStays);
            Spawned(original, made);
            return made;
        }

        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object
        {
            var made = Object.Instantiate(original, position, rotation);
            Spawned(original, made);
            return made;
        }

        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform parent) where T : Object
        {
            var made = Object.Instantiate(original, position, rotation, parent);
            Spawned(original, made);
            return made;
        }

        private static void Spawned(Object original, Object made)
        {
            // 만들어진 것이 어디 놓였는지가 사건의 자리다. 프리팹 이름은 detail 로.
            EngineEvents.Record("spawn", ComponentOf(made), original == null ? null : original.name, null);
        }

        public static void Destroy(Object obj)
        {
            EngineEvents.Record("destroy", ComponentOf(obj), obj == null ? null : obj.name, null);
            Object.Destroy(obj);
        }

        public static void Destroy(Object obj, float t)
        {
            EngineEvents.Record("destroy", ComponentOf(obj), obj == null ? null : obj.name, t);
            Object.Destroy(obj, t);
        }

        private static Component ComponentOf(Object subject)
        {
            switch (subject)
            {
                case Component component:
                    return component;
                case GameObject gameObject:
                    return gameObject != null ? gameObject.transform : null;
            }

            return null;
        }

        // --- SceneManager -----------------------------------------------------------------

        public static void LoadScene(string sceneName)
        {
            EngineEvents.Record("scene.load", null, sceneName, null);
            SceneManager.LoadScene(sceneName);
        }

        public static void LoadScene(int sceneBuildIndex)
        {
            EngineEvents.Record("scene.load", null, "#" + sceneBuildIndex, null);
            SceneManager.LoadScene(sceneBuildIndex);
        }

        public static void LoadScene(string sceneName, LoadSceneMode mode)
        {
            EngineEvents.Record("scene.load", null, sceneName, mode);
            SceneManager.LoadScene(sceneName, mode);
        }

        public static void LoadScene(int sceneBuildIndex, LoadSceneMode mode)
        {
            EngineEvents.Record("scene.load", null, "#" + sceneBuildIndex, mode);
            SceneManager.LoadScene(sceneBuildIndex, mode);
        }

        public static AsyncOperation LoadSceneAsync(string sceneName)
        {
            EngineEvents.Record("scene.load", null, sceneName, null);
            return SceneManager.LoadSceneAsync(sceneName);
        }

        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex)
        {
            EngineEvents.Record("scene.load", null, "#" + sceneBuildIndex, null);
            return SceneManager.LoadSceneAsync(sceneBuildIndex);
        }

        public static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode)
        {
            EngineEvents.Record("scene.load", null, sceneName, mode);
            return SceneManager.LoadSceneAsync(sceneName, mode);
        }

        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex, LoadSceneMode mode)
        {
            EngineEvents.Record("scene.load", null, "#" + sceneBuildIndex, mode);
            return SceneManager.LoadSceneAsync(sceneBuildIndex, mode);
        }

        // --- PlayerPrefs ------------------------------------------------------------------

        public static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            EngineEvents.Record("prefs.set", null, key, value);
        }

        public static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            EngineEvents.Record("prefs.set", null, key, value);
        }

        public static void SetString(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            EngineEvents.Record("prefs.set", null, key, value);
        }

        public static void DeleteKey(string key)
        {
            PlayerPrefs.DeleteKey(key);
            EngineEvents.Record("prefs.delete", null, key, null);
        }

        public static void Save()
        {
            PlayerPrefs.Save();
            EngineEvents.Record("prefs.save", null, null, null);
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteAll();
            EngineEvents.Record("prefs.delete_all", null, null, null);
        }
    }
}
