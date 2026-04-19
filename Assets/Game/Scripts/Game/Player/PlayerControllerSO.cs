using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerControllerSO", menuName = "Player/PlayerControllerSO")]
public class PlayerControllerSO : ScriptableObject
{
    [Header("移动属性")]
    public float moveSpeed = 5.0f;
    public float jumpForce = 16.0f;

    [Header("物理属性")]
    public float gravityScale = 4.0f;

    [Header("地面检测")]
    public Vector2 groundCheckOffset = new Vector2(0, -1);
    public float groundCheckRadius = 0.2f;
}