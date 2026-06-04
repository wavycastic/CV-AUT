using OpenCvSharp;

namespace CvAut;

/// <summary>
/// Các tiện ích xử lý ảnh dùng chung cho nhiều cấu phần trong ứng dụng.
/// </summary>
public static class ImageUtils
{
    /// <summary>
    /// Giới hạn một vùng hình chữ nhật (Rect) để đảm bảo nó nằm trọn vẹn trong biên của bức ảnh.
    /// Giúp tránh các ngoại lệ vỡ khung hình (out of bounds exception) khi crop ảnh bằng OpenCV.
    /// </summary>
    /// <param name="rect">Hình chữ nhật cần giới hạn.</param>
    /// <param name="width">Chiều rộng tối đa của ảnh nguồn.</param>
    /// <param name="height">Chiều cao tối đa của ảnh nguồn.</param>
    /// <returns>Đối tượng Rect mới đã được chuẩn hóa để an toàn không tràn biên.</returns>
    public static Rect ClampRect(Rect rect, int width, int height)
    {
        // Giới hạn giá trị của toạ độ trái (Left) và trên (Top) trong khoảng [0, kích thước ảnh]
        int left = Math.Clamp(rect.Left, 0, width);
        int top = Math.Clamp(rect.Top, 0, height);
        
        // Đảm bảo toạ độ phải (Right) và dưới (Bottom) không vượt quá biên ảnh
        int right = Math.Clamp(rect.Right, left, width);
        int bottom = Math.Clamp(rect.Bottom, top, height);
        
        // Tạo và trả về Rect chuẩn hóa từ các toạ độ biên mới
        return Rect.FromLTRB(left, top, right, bottom);
    }
}
