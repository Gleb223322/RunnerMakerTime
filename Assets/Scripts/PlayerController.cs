using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float forwardSpeed = 10f;
    [SerializeField] float laneDistance = 2.5f;
    [SerializeField] float lateralLerpSpeed = 10f;
    [SerializeField] float jumpForce = 7f;
    [SerializeField] float gravity = -20f;
    [SerializeField] float slideDuration = 1.0f;

    [Header("Controller sizes")]
    [SerializeField] float standingHeight = 2.0f;
    [SerializeField] float slidingHeight = 1.0f;
    [SerializeField] Vector3 standingCenter = new Vector3(0, 1.0f, 0);
    [SerializeField] Vector3 slidingCenter = new Vector3(0, 0.5f, 0);

    [Header("References")]
    [SerializeField] Animator animator;

    CharacterController cc;
    int currentLane = 1; // 0 left, 1 middle, 2 right
    float verticalVelocity = 0f;
    bool isSliding = false;
    bool canMove = true;

    float startX;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        cc.height = standingHeight;
        cc.center = standingCenter;
        startX = transform.position.x;
        Debug.Log("[PlayerController] Started. Using Input System API.");
    }

    void Update()
    {
        if (!enabled || !canMove) return;
        if (Time.timeScale == 0f) return;

        ReadInput();
        ApplyGravity();
        MoveCharacter();
        UpdateAnimator();
    }

    void ReadInput()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
            {
                ChangeLane(-1);
            }
            else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
            {
                ChangeLane(1);
            }

            if ((kb.spaceKey.wasPressedThisFrame) && cc.isGrounded && !isSliding)
            {
                verticalVelocity = jumpForce;
                animator?.SetTrigger("Jump");
            }

            if ((kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) && !isSliding && cc.isGrounded)
            {
                StartCoroutine(DoSlide());
            }

            var gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.dpad.left.wasPressedThisFrame) ChangeLane(-1);
                if (gp.dpad.right.wasPressedThisFrame) ChangeLane(1);
                if (gp.buttonSouth.wasPressedThisFrame && cc.isGrounded && !isSliding)
                {
                    verticalVelocity = jumpForce;
                    animator?.SetTrigger("Jump");
                }
                if (gp.buttonWest.wasPressedThisFrame && !isSliding && cc.isGrounded)
                {
                    StartCoroutine(DoSlide());
                }
            }

            return;
        }
#else
        // Old Input API disabled: enable Input System or set Active Input Handling to "Both" in Player Settings.
#endif
    }

    void ChangeLane(int dir)
    {
        int newLane = Mathf.Clamp(currentLane + dir, 0, 2);
        if (newLane == currentLane) return;
        currentLane = newLane;
        Debug.Log("[PlayerController] Lane -> " + currentLane);
    }

    void ApplyGravity()
    {
        if (cc.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -1f;
            animator?.SetBool("IsGrounded", true);
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
            animator?.SetBool("IsGrounded", false);
        }
    }

    void MoveCharacter()
    {
        float speedMultiplier = 1f;
        if (GameManager.Instance != null) speedMultiplier = GameManager.Instance.SpeedMultiplier;

        Vector3 forwardMove = Vector3.forward * forwardSpeed * speedMultiplier * Time.deltaTime;
        float desiredX = startX + (currentLane - 1) * laneDistance;
        float newX = Mathf.Lerp(transform.position.x, desiredX, lateralLerpSpeed * Time.deltaTime);
        Vector3 lateralMove = new Vector3(newX - transform.position.x, 0f, 0f);
        Vector3 verticalMove = Vector3.up * verticalVelocity * Time.deltaTime;
        cc.Move(forwardMove + lateralMove + verticalMove);
    }

    IEnumerator DoSlide()
    {
        isSliding = true;
        animator?.SetBool("IsSliding", true);

        cc.height = slidingHeight;
        cc.center = slidingCenter;

        yield return new WaitForSeconds(slideDuration);

        cc.height = standingHeight;
        cc.center = standingCenter;
        animator?.SetBool("IsSliding", false);
        isSliding = false;
    }

    void UpdateAnimator()
    {
        Vector3 horizontalVel = new Vector3(cc.velocity.x, 0f, cc.velocity.z);
        animator?.SetFloat("Speed", horizontalVel.magnitude);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null) return;
        if (hit.collider.CompareTag("Obstacle"))
        {
            Debug.Log("[PlayerController] Hit obstacle: " + hit.collider.name);
            GameManager.Instance?.OnPlayerHitObstacle();
        }
    }

    public void SetCanMove(bool v)
    {
        canMove = v;
    }
}
