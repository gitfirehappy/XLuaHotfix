using System.Threading.Tasks;
using UnityEngine;
using XLua;

public class GizmosBridge : MonoBehaviour, IBridge
{
    private LuaTable luaInstance;
    private LuaFunction drawGizmosFunc;
    private LuaFunction drawGizmosSelectedFunc;

    public async Task InitializeAsync(LuaTable luaTable)
    {
        luaInstance = luaTable;
        
        drawGizmosFunc = luaInstance.Get<LuaFunction>("OnDrawGizmos");
        drawGizmosSelectedFunc = luaInstance.Get<LuaFunction>("OnDrawGizmosSelected");
        await Task.CompletedTask;
    }

    #region Gizmos绘制方法

    // 绘制线
    public void DrawLine(Vector3 from, Vector3 to)
    {
        Gizmos.DrawLine(from, to);
    }

    public void DrawLine(Vector3 from, Vector3 to, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawLine(from, to);
        Gizmos.color = originalColor;
    }

    // 绘制球体
    public void DrawSphere(Vector3 center, float radius)
    {
        Gizmos.DrawSphere(center, radius);
    }

    public void DrawSphere(Vector3 center, float radius, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawSphere(center, radius);
        Gizmos.color = originalColor;
    }

    public void DrawWireSphere(Vector3 center, float radius)
    {
        Gizmos.DrawWireSphere(center, radius);
    }

    public void DrawWireSphere(Vector3 center, float radius, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(center, radius);
        Gizmos.color = originalColor;
    }

    // 绘制立方体
    public void DrawCube(Vector3 center, Vector3 size)
    {
        Gizmos.DrawCube(center, size);
    }

    public void DrawCube(Vector3 center, Vector3 size, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawCube(center, size);
        Gizmos.color = originalColor;
    }

    public void DrawWireCube(Vector3 center, Vector3 size)
    {
        Gizmos.DrawWireCube(center, size);
    }

    public void DrawWireCube(Vector3 center, Vector3 size, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, size);
        Gizmos.color = originalColor;
    }

    // 绘制射线
    public void DrawRay(Vector3 from, Vector3 direction)
    {
        Gizmos.DrawRay(from, direction);
    }

    public void DrawRay(Vector3 from, Vector3 direction, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawRay(from, direction);
        Gizmos.color = originalColor;
    }

    public void DrawRay(Ray ray)
    {
        Gizmos.DrawRay(ray);
    }

    public void DrawRay(Ray ray, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawRay(ray);
        Gizmos.color = originalColor;
    }

    // 绘制图标
    public void DrawIcon(Vector3 center, string iconName)
    {
        Gizmos.DrawIcon(center, iconName);
    }

    public void DrawIcon(Vector3 center, string iconName, bool allowScaling)
    {
        Gizmos.DrawIcon(center, iconName, allowScaling);
    }

    // 绘制网格
    public void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Gizmos.DrawMesh(mesh, position, rotation, scale);
    }

    public void DrawWireMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Gizmos.DrawWireMesh(mesh, position, rotation, scale);
    }

    // 绘制视锥体
    public void DrawFrustum(Vector3 center, float fov, float maxRange, float minRange, float aspect)
    {
        Gizmos.DrawFrustum(center, fov, maxRange, minRange, aspect);
    }

    #endregion

    #region 辅助绘制方法

    // 绘制圆形（2D）
    public void DrawCircle(Vector3 center, float radius, Color color, int segments = 32)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;

        float angleStep = 360f / segments;
        Vector3 prevPoint = center + new Vector3(radius, 0, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 nextPoint = center + new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0
            );

            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        Gizmos.color = originalColor;
    }

    // 绘制扇形
    public void DrawSector(Vector3 center, Vector3 direction, float radius, float angle, Color color, int segments = 16)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;

        float halfAngle = angle * 0.5f;
        float startAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - halfAngle;
        float endAngle = startAngle + angle;

        Vector3 prevPoint = center;
        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = (startAngle + (endAngle - startAngle) * i / segments) * Mathf.Deg2Rad;
            Vector3 nextPoint = center + new Vector3(
                Mathf.Cos(currentAngle) * radius,
                Mathf.Sin(currentAngle) * radius,
                0
            );

            if (i == 0 || i == segments)
            {
                Gizmos.DrawLine(center, nextPoint);
            }

            if (i > 0)
            {
                Gizmos.DrawLine(prevPoint, nextPoint);
            }

            prevPoint = nextPoint;
        }

        Gizmos.color = originalColor;
    }

    // 绘制矩形区域
    public void DrawRect(Rect rect, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;

        Vector3 bottomLeft = new Vector3(rect.xMin, rect.yMin, 0);
        Vector3 topLeft = new Vector3(rect.xMin, rect.yMax, 0);
        Vector3 topRight = new Vector3(rect.xMax, rect.yMax, 0);
        Vector3 bottomRight = new Vector3(rect.xMax, rect.yMin, 0);

        Gizmos.DrawLine(bottomLeft, topLeft);
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);

        Gizmos.color = originalColor;
    }

    // 绘制3D框线
    public void DrawBounds(Bounds bounds, Color color)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;

        Vector3 center = bounds.center;
        Vector3 size = bounds.size;

        // 计算8个顶点
        Vector3 frontBottomLeft = center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, -size.z * 0.5f);
        Vector3 frontBottomRight = center + new Vector3(size.x * 0.5f, -size.y * 0.5f, -size.z * 0.5f);
        Vector3 frontTopLeft = center + new Vector3(-size.x * 0.5f, size.y * 0.5f, -size.z * 0.5f);
        Vector3 frontTopRight = center + new Vector3(size.x * 0.5f, size.y * 0.5f, -size.z * 0.5f);
        Vector3 backBottomLeft = center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, size.z * 0.5f);
        Vector3 backBottomRight = center + new Vector3(size.x * 0.5f, -size.y * 0.5f, size.z * 0.5f);
        Vector3 backTopLeft = center + new Vector3(-size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
        Vector3 backTopRight = center + new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);

        // 绘制前面
        Gizmos.DrawLine(frontBottomLeft, frontBottomRight);
        Gizmos.DrawLine(frontBottomRight, frontTopRight);
        Gizmos.DrawLine(frontTopRight, frontTopLeft);
        Gizmos.DrawLine(frontTopLeft, frontBottomLeft);

        // 绘制后面
        Gizmos.DrawLine(backBottomLeft, backBottomRight);
        Gizmos.DrawLine(backBottomRight, backTopRight);
        Gizmos.DrawLine(backTopRight, backTopLeft);
        Gizmos.DrawLine(backTopLeft, backBottomLeft);

        // 绘制侧面连线
        Gizmos.DrawLine(frontBottomLeft, backBottomLeft);
        Gizmos.DrawLine(frontBottomRight, backBottomRight);
        Gizmos.DrawLine(frontTopLeft, backTopLeft);
        Gizmos.DrawLine(frontTopRight, backTopRight);

        Gizmos.color = originalColor;
    }

    // 绘制箭头
    public void DrawArrow(Vector3 from, Vector3 to, Color color, float headLength = 0.25f, float headAngle = 20f)
    {
        Color originalColor = Gizmos.color;
        Gizmos.color = color;

        // 绘制主线
        Gizmos.DrawLine(from, to);

        // 计算箭头方向
        Vector3 direction = (to - from).normalized;

        // 计算箭头头部顶点
        Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + headAngle, 0) * Vector3.forward;
        Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - headAngle, 0) * Vector3.forward;

        // 绘制箭头头部
        Gizmos.DrawLine(to, to + right * headLength);
        Gizmos.DrawLine(to, to + left * headLength);

        Gizmos.color = originalColor;
    }

    // 绘制网格检测区域
    public void DrawOverlapArea(Vector3 center, Vector2 size, Color normalColor, Color overlapColor, LayerMask layerMask)
    {
        Collider2D[] colliders = Physics2D.OverlapBoxAll(center, size, 0, layerMask);
        bool hasOverlap = colliders.Length > 0;

        DrawWireCube(center, size, hasOverlap ? overlapColor : normalColor);
    }

    // 绘制圆形检测区域
    public void DrawOverlapCircle(Vector3 center, float radius, Color normalColor, Color overlapColor, LayerMask layerMask)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(center, radius, layerMask);
        bool hasOverlap = colliders.Length > 0;

        DrawWireSphere(center, radius, hasOverlap ? overlapColor : normalColor);
    }

    #endregion

    #region 工具方法

    // 设置Gizmos颜色
    public void SetColor(Color color)
    {
        Gizmos.color = color;
    }

    // 设置Gizmos矩阵
    public void SetMatrix(Matrix4x4 matrix)
    {
        Gizmos.matrix = matrix;
    }

    // 重置Gizmos矩阵
    public void ResetMatrix()
    {
        Gizmos.matrix = Matrix4x4.identity;
    }

    #endregion

    #region Unity生命周期

    void OnDrawGizmos()
    {
        drawGizmosFunc?.Call(luaInstance);
    }

    void OnDrawGizmosSelected()
    {
        drawGizmosSelectedFunc?.Call(luaInstance);
    }

    public void OnDestroy()
    {
        // 清理Lua引用
        drawGizmosFunc?.Dispose();
        drawGizmosSelectedFunc?.Dispose();
    }

    #endregion
}
