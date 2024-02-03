using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.UserDataTasks;
using Windows.Foundation;
using WinRT;

namespace BluetoothHeartrateModule
{
    public class AsyncHelper
    {
        private readonly BluetoothHeartrateModule module;
        public AsyncHelper(BluetoothHeartrateModule module)
        {
            this.module = module;
        }

        public Task<T?> WaitAsync<T>(IAsyncOperation<T> asyncOperation, AsyncTask taskType, CancellationTokenSource cancelToken)
        {
            return WaitAsync(asyncOperation.AsTask(), taskType, cancelToken);
        }

        public Task<T?> WaitAsync<T>(IAsyncOperation<T> asyncOperation, AsyncTask taskType)
        {
            return WaitAsync(asyncOperation.AsTask(), taskType);
        }

        public async Task WaitAsyncVoid(Task task, AsyncTask taskType, CancellationTokenSource cancelToken)
        {
            var timeout = GetTaskTimeout(taskType);
            try
            {
                await task.WaitAsync(timeout, cancelToken.Token);
            }
            catch (TimeoutException)
            {
                LogTimeout(taskType, timeout);
            }
        }

        public async Task WaitAsyncVoid(Task task, AsyncTask taskType)
        {
            var timeout = GetTaskTimeout(taskType);
            try
            {
                await task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                LogTimeout(taskType, timeout);
            }
        }

        public async Task<T?> WaitAsync<T>(Task<T> task, AsyncTask taskType)
        {
            var timeout = GetTaskTimeout(taskType);
            try
            {
                return await task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                LogTimeout(taskType, timeout);
            }
            return default;
        }

        public async Task<T?> WaitAsync<T>(Task<T> task, AsyncTask taskType, CancellationTokenSource cancelToken)
        {
            var timeout = GetTaskTimeout(taskType);
            try
            {
                return await task.WaitAsync(timeout, cancelToken.Token);
            }
            catch (TimeoutException)
            {
                LogTimeout(taskType, timeout);
            }
            return default;
        }

        internal void LogTimeout(AsyncTask taskType, TimeSpan timeout)
        {
            module.LogDebug($"Task {taskType} timed out after {timeout.TotalSeconds}s");
        }

        internal static TimeSpan GetTaskTimeout(AsyncTask taskType)
        {
            return taskType switch
            {
                AsyncTask.WriteCharacteristicConfigDescriptor or AsyncTask.ReadHeartRateValue => TimeSpan.FromSeconds(1),
                _ => TimeSpan.FromSeconds(5),
            };
        }
    }

    public enum AsyncTask {
        GetDeviceName,
        GetDeviceFromAddress,
        GetDeviceInfo,
        SetCurrentDevice,
        GetHeartRateService,
        GetGenericAccessService,
        GetHeartRateCharacteristic,
        GetDeviceNameCharacteristic,
        WriteCharacteristicConfigDescriptor,
        ReadHeartRateValue,
        CloseWebsocketConnection,
    }
}
