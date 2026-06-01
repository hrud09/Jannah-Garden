using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif


namespace LayerLab
{
    public class PanelControl : MonoBehaviour
    {
        private int page = 0;
        private bool isReady = false;
        [SerializeField] private List<GameObject> panels = new List<GameObject>();
        private TextMeshProUGUI textTitle;
        [SerializeField] private Transform panelTransform;
        [SerializeField] private Button buttonPrev;
        [SerializeField] private Button buttonNext;

        private void Start()
        {
            textTitle = transform.GetComponentInChildren<TextMeshProUGUI>();
            buttonPrev.onClick.AddListener(Click_Prev);
            buttonNext.onClick.AddListener(Click_Next);

            foreach (Transform t in panelTransform)
            {
                panels.Add(t.gameObject);
                t.gameObject.SetActive(false);
            }

            panels[page].SetActive(true);
            isReady = true;

            CheckControl();
        }

        void Update()
        {
            if (panels.Count <= 0 || !isReady) return;

            bool leftPressed = false;
            bool rightPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                leftPressed = Keyboard.current.leftArrowKey.wasPressedThisFrame;
                rightPressed = Keyboard.current.rightArrowKey.wasPressedThisFrame;
            }
#else
            leftPressed = Input.GetKeyDown(KeyCode.LeftArrow);
            rightPressed = Input.GetKeyDown(KeyCode.RightArrow);
#endif

            if (leftPressed)
                Click_Prev();
            else if (rightPressed)
                Click_Next();
        }

        //Click_Prev
        public void Click_Prev()
        {
            if (page <= 0 || !isReady) return;

            panels[page].SetActive(false);
            panels[page -= 1].SetActive(true);
            textTitle.text = panels[page].name;
            CheckControl();
        }

        //Click_Next
        public void Click_Next()
        {
            if (page >= panels.Count - 1) return;

            panels[page].SetActive(false);
            panels[page += 1].SetActive(true);
            CheckControl();
        }

        void SetArrowActive()
        {
            buttonPrev.gameObject.SetActive(page > 0);
            buttonNext.gameObject.SetActive(page < panels.Count - 1);
        }

        //SetTitle, SetArrow Active
        private void CheckControl()
        {
            textTitle.text = panels[page].name.Replace("_", " ");
            SetArrowActive();
        }
    }
}
