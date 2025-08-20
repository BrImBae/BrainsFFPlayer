using FFmpeg.AutoGen;
using System.Drawing;

namespace BrainsFFPlayer.FFmpeg
{
    public enum DecodeStatus
    {
        Success,
        TryAgain,    // 일시적 오류: 상위 루프에서 유예창 내 반복 재시도
        StreamEnded  // 실제 종료 또는 유예 초과
    }

    public unsafe class VideoInfo
    {
        /// <summary>
        /// 비디오 재생 길이
        /// <para> (AVFormatContext의 값 참조) </para>
        /// </summary>
        public long TotalDuration { get; set; }

        public Size FrameSize { get; set; }
        public AVRational FrameRate { get; set; }

        /// <summary>
        /// FFmpeg가 파일을 열고 스트림 정보를 분석할 때, 파일의 특정 크기만큼 데이터를 읽어서 그 데이터를 분석
        /// 스트림 분석 정확도와 처리 속도 간의 균형을 조정하는 데 사용
        /// <para> 작은 probesize 값: 분석 속도를 높일 수 있지만, 복잡한 포맷의 경우 스트림 정보를 정확히 분석하지 못할 수 있음 </para>
        /// <para> 큰 probesize 값: 더 많은 데이터를 검사하여 스트림 정보를 더 정확하게 분석할 수 있지만, 초기 분석 시간이 길어질 수 있음. </para>
        /// <para> 500000은 500KB를 의미하며, 입력 파일의 초기 500KB를 검사 </para>
        /// <para> (AVFormatContext의 값 참조) </para>
        /// </summary>
        public long ProbeSize { get; set; }

        /// <summary>
        /// 전체 파일 컨테이너의 평균 비트레이트 - 전체 파일의 대역폭 분석
        /// (AVFormatContext의 값 참조)
        /// </summary>
        public long AverageBitrate { get; set; }

        /// <summary>
        /// 개별 스트림(코덱) 비트레이트, 코덱 수준에서 명시적 설정 가능 - 스트림 화질 제어
        /// (AVCodecContext의 값 참조)
        /// </summary>
        public long Bitrate { get; set; }

        /// <summary>
        /// 비디오의 키 프레임 배치와 관련된 중요한 파라미터로, 파일 크기, 압축 효율성, 랜덤 액세스 성능 사이에서 균형을 맞추는 데 사용
        /// <para> 스트리밍: 작은 GopSize(30~60): 빠른 탐색과 안정적인 네트워크 전송. </para>
        /// <para> 고정 파일 인코딩: 큰 GopSize(100~250): 더 높은 압축 효율로 파일 크기 감소 </para>        
        /// <para> (AVCodecContext의 값 참조) </para>
        /// </summary>
        public int GopSize { get; set; }

        /// <summary>
        /// (AVCodecContext의 값 참조)
        /// </summary>
        public AVCodecID CodecID { get; set; }

        /// <summary>
        /// (AVCodecContext의 값 참조)
        /// </summary>
        public AVPixelFormat PixcelFormat { get; set; }

        /// <summary>
        /// H.264 코덱은 다양한 용도와 하드웨어 호환성을 위해 여러 프로파일(Profile)을 지원
        /// <para> (FF_PROFILE_H264_BASELINE; 66): 주로 저대역폭 환경에서 사용 </para>
        /// <para> (FF_PROFILE_H264_MAIN; 77): Baseline보다 높은 압축 효율, 방송 및 일반 동영상 저장에 적합 </para>
        /// <para> (FF_PROFILE_H264_HIGH; 100): 높은 압축 효율과 화질 제공, Blu-ray, 고화질 스트리밍, 전문 동영상 </para>
        /// <para>  </para>
        /// </summary>
        public int Profile { get; set; }

        /// <summary>
        /// 비트레이트, 해상도, 프레임 속도의 상한선
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 최소 퀀타이저 값(압축 품질), 낮은 qmin 값은 더 높은 품질을 의미
        /// </summary>
        public int QMin { get; set; }

        /// <summary>
        /// 최대 퀀타이저 값(압축 품질), 높은 qmax 값은 압축을 늘려 파일 크기를 줄일 수 있음.
        /// </summary>
        public int QMax { get; set; }

        /// <summary>
        /// 비디오 인코딩에서 B-프레임(Bi-directional frames)의 최대 개수를 설정하는 값.
        /// <para> 이전과 이후의 프레임을 참조하여 압축 효율을 높이는 프레임 타입으로, 인코딩 효율성을 높이고 파일 크기를 줄이는 데 도움 </para>
        /// <para> 인코딩 시 필수 요소는 아니지만, B-프레임을 사용할 경우 인코딩 효율성과 품질을 조정하는 중요한 요소 </para>
        /// <para> B-프레임을 많이 사용하면 디코딩 복잡도가 증가 </para>
        /// <para> 0: B-프레임을 사용하지 않음, 1 이상: 푀대 B-프레임 수 설정 </para>
        /// <para> (AVCodecContext의 값 참조) </para>
        /// </summary>
        public int MaxBFrames { get; set; }

        /// <summary>
        /// 비디오 스트림의 픽셀의 종횡비
        /// <para> 인코딩 시 필수적인 요소는 아니지만, 올바르게 설정하지 않으면 비디오가 왜곡될 수 있음 </para>
        /// <para> (AVStream 또는 AVCodecContext의 값 참조) </para>
        /// </summary>
        public AVRational SampleAspectRatio { get; set; }

        /// <summary>
        /// 비디오 파일의 시간 정보를 표현하는 기본 단위. 분수 형태로 나타내며, timebase = num / den 형태로 표현
        /// <para> den 값이 클수록 타임스탬프의 정밀도가 높아지며, 작은 단위로 시간 간격을 표현 </para>
        /// <para> (AVStream의 값 참조) </para>
        /// </summary>
        public AVRational Timebase { get; set; }

        /// <summary>
        /// 멀티스레드 인코딩의 스레드 수를 설정
        /// </summary>
        public int ThreadCount { get; set; }

        /// <summary>
        /// 레이트 컨트롤을 위한 버퍼 크기 설정 (e.g. 1000000; // 1MB 버퍼)
        /// <para> 크기가 클수록: 버퍼 충전 여유가 늘어나며, 비트레이트 변동을 더 완만하게 허용 </para>
        /// <para> 크기가 작을수록: 즉각적인 비트레이트 반응이 가능하지만, 품질 변동이 커질 수 있음 </para>
        /// </summary>
        public int RcBufferSize { get; set; }

        /// <summary>
        /// 레이트 컨트롤을 위한 최대 비트레이트 설정 (e.g. 4000000; // 4Mbps 최대 비트레이트)
        /// </summary>
        public long RcMaxRate { get; set; }

        /// <summary>
        /// 낮은 지연 시간을 요구하는 스트리밍 환경에서 중요한 매개변수 (0; // 최소 지연 시간 설정)
        /// </summary>
        public int Delay { get; set; }
    }
}
