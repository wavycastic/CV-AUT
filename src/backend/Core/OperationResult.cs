namespace CvAut.Core
{
    /// <summary>
    /// Kết quả của một thao tác hoặc chu kỳ tự động hóa.
    /// </summary>
    public enum OperationResult
    {
        /// <summary>Thực thi thành công hoàn toàn.</summary>
        Success,

        /// <summary>Thực thi hoàn thành một phần (có bước tùy chọn bị bỏ qua hoặc lỗi nhẹ).</summary>
        PartialSuccess,

        /// <summary>Thao tác bị bỏ qua do thiếu nhận diện hoặc không đủ điều kiện.</summary>
        Skipped,

        /// <summary>Thao tác thất bại do lỗi không thể khắc phục hoặc timeout.</summary>
        Failed,

        /// <summary>Thao tác bị hủy bởi người dùng hoặc hệ thống.</summary>
        Cancelled
    }
}
