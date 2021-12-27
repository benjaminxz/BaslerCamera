using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Basler.Pylon;

namespace Basler
{
    public class BaslerCamera
    {
        private Camera _camera = null;
        private class BufferTransferStrategy
        {
            public Action<long, Bitmap> BitmapReceived { get; set; }
            public Action<long, IntPtr, int, int> BitmapReceived2 { get; set; }
        }
        private BufferTransferStrategy _bufferTransferStrategy;

        public bool open(string serialNumber)
        {
            return connectCamera(serialNumber) == 0;
        }
        /// <summary>
        /// �������������-1Ϊʧ�ܣ�0Ϊ�ɹ�
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private int connectCamera(string id)
        {
            /*��ȡ��������豸*/
            List<ICameraInfo> allCameras = CameraFinder.Enumerate();
            for (int i = 0; i < allCameras.Count; i++)
            {
                if (allCameras[i][CameraInfoKey.SerialNumber] == id)
                {
                    //�����ǰ�����Ϣ�����к���ָ�������кţ���ʵ���������
                    _camera = new Camera(allCameras[i]);
                    _camera.Open();//�����
                    _camera.ConnectionLost += cameraConnectionLost;
                    _camera.StreamGrabber.ImageGrabbed += streamGrabberImageGrabbed;
                    _camera.CameraOpened += cameraOpened;
                    return 0;
                }
            }
            return -1;
        }

        public bool start(Action<long, Bitmap> bitmapReceived)
        {
            return start(bitmapReceived, null);
        }

        public bool start(Action<long, IntPtr, int, int> bitmapReceived2)
        {
            return start(null, bitmapReceived2);
        }

        private bool start(Action<long, Bitmap> bitmapReceived, Action<long, IntPtr, int, int> bitmapReceived2)
        {
            if (_camera == null)
                return false;
            _bufferTransferStrategy = new BufferTransferStrategy();
            _bufferTransferStrategy.BitmapReceived = bitmapReceived;
            _bufferTransferStrategy.BitmapReceived2 = bitmapReceived2;
            _camera.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);

            return true;
        }
        /// <summary>
        /// ֹͣ����ɼ�
        /// </summary>
        public void stopCamera()
        {
            _camera.StreamGrabber.Stop();
        }
        /// <summary>
        /// �ر����
        /// </summary>
        public void closeCamera()
        {
            _camera.Close();
            _camera.Dispose();
            _camera = null;
        }

        private void cameraConnectionLost(object sender, EventArgs e)
        {
            _camera.StreamGrabber.Stop();
            closeCamera();
        }

        /// <summary>
        /// Configure the camera by using the CameraOpened event to call configuration methods. This has the advantage that the camera is always parameterized correctly when opened.
        /// ʹ��CameraOpened�¼����������÷�����������������������ĺô�������ڴ�ʱ���ǲ�������ȷ��
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void cameraOpened(object sender, EventArgs e)
        {
            _camera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);
        }

        private void streamGrabberImageGrabbed(object sender, ImageGrabbedEventArgs e)
        {
            IGrabResult grabResult = e.GrabResult;
            if (grabResult.IsValid && grabResult.GrabSucceeded)
            {

                if (_bufferTransferStrategy.BitmapReceived != null)
                {
                    if (grabResult.PixelTypeValue == PixelType.Mono8)
                    {
                        var imgData = (byte[])grabResult.PixelData;
                        //////////д��2  ʹ��ָ��ķ�ʽ 
                        //int size2 = grabResult.Width * grabResult.Height;
                        //byte[] managedBuffer = new byte[size2];
                        //System.Runtime.InteropServices.Marshal.Copy(grabResult.PixelDataPointer, managedBuffer, 0, size2);
                        //imgData = managedBuffer;
                        //////////д��2
                        var bitmap1 = new Bitmap(grabResult.Width, grabResult.Height, PixelFormat.Format8bppIndexed);
                        BitmapData bmpData = bitmap1.LockBits(new Rectangle(0, 0, bitmap1.Width, bitmap1.Height), ImageLockMode.ReadWrite, bitmap1.PixelFormat);
                        IntPtr ptrBmp = bmpData.Scan0;

                        if (bitmap1.PixelFormat == PixelFormat.Format8bppIndexed)
                        {
                            ColorPalette colorPalette = bitmap1.Palette;
                            for (int i = 0; i < 256; i++)
                            {
                                colorPalette.Entries[i] = Color.FromArgb(i, i, i);
                            }
                            bitmap1.Palette = colorPalette;
                        }

                        int imageStride = grabResult.Width;
                        if (imageStride == bmpData.Stride)
                        {
                            Marshal.Copy(imgData, 0, ptrBmp, bmpData.Stride * bitmap1.Height);
                        }
                        else
                        {
                            for (int i = 0; i < bitmap1.Height; ++i)
                            {
                                Marshal.Copy(imgData, i * imageStride, new IntPtr(ptrBmp.ToInt64() + i * bmpData.Stride), grabResult.Width);
                            }
                        }
                        bitmap1.UnlockBits(bmpData);
                        _bufferTransferStrategy.BitmapReceived(grabResult.ID, bitmap1);
                    }
                    else if (grabResult.PixelTypeValue == PixelType.BGR8packed)
                    {
                        int size = grabResult.Width * grabResult.Height * 3;
                        byte[] managedBuffer = new byte[size];
                        System.Runtime.InteropServices.Marshal.Copy(grabResult.PixelDataPointer, managedBuffer, 0, size);
                        var bitmap = new Bitmap(grabResult.Width, grabResult.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        System.Drawing.Imaging.BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, grabResult.Width, grabResult.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        System.Runtime.InteropServices.Marshal.Copy(managedBuffer, 0, bitmapData.Scan0, size);
                        bitmap.UnlockBits(bitmapData);
                        _bufferTransferStrategy.BitmapReceived(grabResult.ID, bitmap);
                    }
                }
                else if (_bufferTransferStrategy.BitmapReceived2 != null)
                {
                    _bufferTransferStrategy.BitmapReceived2.Invoke(grabResult.ID, grabResult.PixelDataPointer, grabResult.Width, grabResult.Height);
                }
            }
        }
        /// <summary>
        /// ������������
        /// </summary>
        public void triggerSoftware()
        {
            try
            {
                if (_camera.StreamGrabber.IsGrabbing)
                {
                    _camera.ExecuteSoftwareTrigger();
                }
            }
            catch (Exception e)
            {
                stopCamera();
                closeCamera();
            }
        }
        /// <summary>
        /// ����ͼ��߶�
        /// </summary>
        /// <param name="height"></param>
        public void setHeight(long height)
        {
            _camera.Parameters[PLCamera.Height].TrySetValue(height);
        }
        /// <summary>
        /// ����ͼ����
        /// </summary>
        /// <param name="width"></param>
        public void setWidth(long width)
        {
            _camera.Parameters[PLCamera.Width].TrySetValue(width);
        }
        /// <summary>
        /// ����ͼ��ˮƽƫ��
        /// </summary>
        /// <param name="offsetX"></param>
        public void setOffsetX(long offsetX)
        {
            _camera.Parameters[PLCamera.OffsetX].TrySetValue(offsetX);
        }
        /// <summary>
        /// ����ͼ����ֱƫ��
        /// </summary>
        /// <param name="offsetY"></param>
        public void setOffsetY(long offsetY)
        {
            _camera.Parameters[PLCamera.OffsetY].TrySetValue(offsetY);
        }
        /// <summary>
        /// ���ô���ģʽ
        /// </summary>
        /// <param name="isOn"></param>
        public void setTriggerModeOnOff(bool isOn)
        {
            if (isOn)
            {
                _camera.Parameters[PLCamera.TriggerMode].TrySetValue("On");
            }
            else
            {
                _camera.Parameters[PLCamera.TriggerMode].TrySetValue("Off");
            }
        }
        /// <summary>
        /// �ж�ͼ���Ƿ�Ϊ�Ҷȸ�ʽ
        /// </summary>
        /// <param name="iGrabResult"></param>
        /// <returns></returns>
        private Boolean IsMonoData(IGrabResult iGrabResult)
        {
            switch (iGrabResult.PixelTypeValue)
            {
                case PixelType.Mono1packed:
                case PixelType.Mono2packed:
                case PixelType.Mono4packed:
                case PixelType.Mono8:
                case PixelType.Mono8signed:
                case PixelType.Mono10:
                case PixelType.Mono10p:
                case PixelType.Mono10packed:
                case PixelType.Mono12:
                case PixelType.Mono12p:
                case PixelType.Mono12packed:
                case PixelType.Mono16:
                    return true;
                default:
                    return false;
            }
        }

        public long getExposureTime()
        {
            return (long)_camera.Parameters[PLCamera.ExposureTimeRaw].GetValue();
        }

        public void setExposureTime(long value)
        {
            // Some camera models may have auto functions enabled. To set the ExposureTime value to a specific value,
            // the ExposureAuto function must be disabled first (if ExposureAuto is available).
            _camera.Parameters[PLCamera.ExposureAuto].TrySetValue(PLCamera.ExposureAuto.Off); // Set ExposureAuto to Off if it is writable.
            _camera.Parameters[PLCamera.ExposureMode].TrySetValue(PLCamera.ExposureMode.Timed); // Set ExposureMode to Timed if it is writable.
            // In previous SFNC versions, ExposureTimeRaw is an integer parameter,��λus
            // integer parameter�����ݣ�����֮ǰ����Ҫ������Чֵ���ϣ�������ܻᱨ��
            long min = _camera.Parameters[PLCamera.ExposureTimeRaw].GetMinimum();
            long max = _camera.Parameters[PLCamera.ExposureTimeRaw].GetMaximum();
            long incr = _camera.Parameters[PLCamera.ExposureTimeRaw].GetIncrement();
            if (value < min)
            {
                value = min;
            }
            else if (value > max)
            {
                value = max;
            }
            else
            {
                value = min + (((value - min) / incr) * incr);
            }
            _camera.Parameters[PLCamera.ExposureTimeRaw].SetValue(value);
        }
    }
}
