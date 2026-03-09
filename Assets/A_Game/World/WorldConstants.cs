/// <summary>
/// 월드 전체에서 공통으로 사용하는 상수를 모아 둔 클래스이다.
///
/// 이 프로젝트는 마칭 큐브 기반 지형이므로 "셀(cell)"과 "샘플(sample)"을 구분하는 것이 중요하다.
///
/// - 셀: 실제 공간을 채우는 한 칸
/// - 샘플: 셀의 코너에 놓인 density 값
///
/// 예를 들어 X축으로 셀이 16칸 있으면, 그 경계를 표현하기 위해 샘플은 17개가 필요하다.
/// 그래서 청크의 셀 크기는 16 x 256 x 16이지만, density 샘플 크기는 17 x 257 x 17이다.
/// </summary>
public static class WorldConstants
{
    /// <summary>
    /// 청크의 X축 셀 개수이다.
    /// </summary>
    public const int ChunkSizeX = 16;

    /// <summary>
    /// 청크의 Y축 셀 개수이다.
    /// </summary>
    public const int ChunkSizeY = 256;

    /// <summary>
    /// 청크의 Z축 셀 개수이다.
    /// </summary>
    public const int ChunkSizeZ = 16;

    /// <summary>
    /// 서브청크 한 변의 길이이다.
    /// 현재는 16 x 16 x 16 서브청크를 사용한다.
    /// </summary>
    public const int SubChunkSize = 16;

    /// <summary>
    /// 청크 하나가 가지는 서브청크 개수이다.
    /// 256 높이를 16씩 나누면 16개가 된다.
    /// </summary>
    public const int SubChunkCount = 16;

    /// <summary>
    /// X축 density 샘플 개수이다.
    /// </summary>
    public const int SampleSizeX = ChunkSizeX + 1;

    /// <summary>
    /// Y축 density 샘플 개수이다.
    /// </summary>
    public const int SampleSizeY = ChunkSizeY + 1;

    /// <summary>
    /// Z축 density 샘플 개수이다.
    /// </summary>
    public const int SampleSizeZ = ChunkSizeZ + 1;

    /// <summary>
    /// 청크 하나의 총 셀 개수이다.
    /// </summary>
    public const int ChunkCellCount = ChunkSizeX * ChunkSizeY * ChunkSizeZ;

    /// <summary>
    /// 청크 하나의 총 density 샘플 개수이다.
    /// </summary>
    public const int ChunkSampleCount = SampleSizeX * SampleSizeY * SampleSizeZ;

    /// <summary>
    /// 서브청크 하나의 총 셀 개수이다.
    /// </summary>
    public const int SubChunkCellCount = SubChunkSize * SubChunkSize * SubChunkSize;

    /// <summary>
    /// density가 이 값을 넘으면 "표면 안쪽"으로 간주하는 기준값이다.
    /// </summary>
    public const byte SurfaceThreshold = 128;

    /// <summary>
    /// 완전히 비어 있는 상태를 뜻하는 density 값이다.
    /// </summary>
    public const byte EmptyDensity = 0;

    /// <summary>
    /// 완전히 가득 찬 상태를 뜻하는 density 값이다.
    /// </summary>
    public const byte FullDensity = 255;
    public const byte EmptyFluidLevel = 0;
    public const byte FullFluidLevel = 255;
}
