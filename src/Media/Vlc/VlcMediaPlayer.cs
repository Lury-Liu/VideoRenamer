using System;
using System.Runtime.InteropServices;

namespace VideoRenamer
{
    // libvlc 播放器高层封装：媒体/播放器生命周期 + 播放控制 + 进度查询。
    // libvlc instance 进程级共享（加载 plugins 开销大）；每个实例持有独立
    // media_player。无 WinForms 依赖（HWND 由调用方传入），供 App 层控件使用。
    public sealed class VlcMediaPlayer : IDisposable
    {
        private static readonly object InstanceSync = new object();
        private static IntPtr sharedInstance = IntPtr.Zero;
        private static bool runtimeInitialized;

        private IntPtr player = IntPtr.Zero;
        private bool disposed;

        // 进程级初始化：把 libvlc 目录加入 DLL 搜索路径并创建共享实例。
        // 幂等；libvlcDirectory 为 libvlc.dll/libvlccore.dll/plugins 所在目录。
        public static bool InitializeRuntime(string libvlcDirectory)
        {
            lock (InstanceSync)
            {
                if (runtimeInitialized)
                {
                    return sharedInstance != IntPtr.Zero;
                }

                runtimeInitialized = true;
                try
                {
                    if (!string.IsNullOrWhiteSpace(libvlcDirectory))
                    {
                        VlcNative.SetDllDirectory(libvlcDirectory);
                    }
                    sharedInstance = VlcNative.libvlc_new(0, null);
                    return sharedInstance != IntPtr.Zero;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsAvailable
        {
            get { return sharedInstance != IntPtr.Zero; }
        }

        public void LoadMedia(string path)
        {
            ThrowIfDisposed();
            if (sharedInstance == IntPtr.Zero && !runtimeInitialized)
            {
                InitializeRuntime(VlcLocator.Resolve());
            }

            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_stop(player);
            }

            if (sharedInstance == IntPtr.Zero || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            IntPtr newMedia = VlcNative.libvlc_media_new_path(sharedInstance, path);
            if (player == IntPtr.Zero)
            {
                player = VlcNative.libvlc_media_player_new_from_media(newMedia);
            }
            else
            {
                VlcNative.libvlc_media_player_set_media(player, newMedia);
            }
            // player 已持有 media 引用，释放本地引用，生命周期交由 player 管理。
            VlcNative.libvlc_media_release(newMedia);
        }

        public void SetHwnd(IntPtr hwnd)
        {
            ThrowIfDisposed();
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_set_hwnd(player, hwnd);
            }
        }

        public void Play()
        {
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_play(player);
            }
        }

        public void Pause()
        {
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_pause(player);
            }
        }

        public void Stop()
        {
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_stop(player);
            }
        }

        public bool IsPlaying
        {
            get { return player != IntPtr.Zero && VlcNative.libvlc_media_player_is_playing(player) != 0; }
        }

        public void SeekTime(long milliseconds)
        {
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_set_time(player, milliseconds);
            }
        }

        public void SeekPosition(float position)
        {
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_set_position(player, position);
            }
        }

        public long TimeMilliseconds
        {
            get { return player == IntPtr.Zero ? 0 : VlcNative.libvlc_media_player_get_time(player); }
        }

        public long LengthMilliseconds
        {
            get { return player == IntPtr.Zero ? 0 : VlcNative.libvlc_media_player_get_length(player); }
        }

        public float Position
        {
            get { return player == IntPtr.Zero ? 0f : VlcNative.libvlc_media_player_get_position(player); }
        }

        public void SetVolume(int volume)
        {
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_audio_set_volume(player, volume);
            }
        }

        public void SetMute(bool mute)
        {
            if (player != IntPtr.Zero)
            {
                VlcNative.libvlc_audio_set_mute(player, mute ? 1 : 0);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("VlcMediaPlayer");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            if (player != IntPtr.Zero)
            {
                try
                {
                    VlcNative.libvlc_media_player_stop(player);
                }
                catch
                {
                }
                try
                {
                    VlcNative.libvlc_media_player_release(player);
                }
                catch
                {
                }
                player = IntPtr.Zero;
            }
        }
    }
}
