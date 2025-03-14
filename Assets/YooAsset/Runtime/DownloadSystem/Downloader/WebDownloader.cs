﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace YooAsset
{
	internal sealed class WebDownloader : DownloaderBase
	{
		private enum ESteps
		{
			None,
			PrepareDownload,
			CreateDownloader,
			CheckDownload,
			VerifyTempFile,
			WaitingVerifyTempFile,
			CachingFile,
			TryAgain,
			Done,
		}

		private bool _keepDownloadHandleLife = false;
		private DownloadHandlerAssetBundle _downloadhandler;
		private ESteps _steps = ESteps.None;


		public WebDownloader(BundleInfo bundleInfo, int failedTryAgain, int timeout) : base(bundleInfo, failedTryAgain, timeout)
		{
		}
		public override void SendRequest(params object[] param)
		{
			if (_steps == ESteps.None)
			{
				_keepDownloadHandleLife = (bool)param[0];
				_steps = ESteps.PrepareDownload;
			}
		}
		public override void Update()
		{
			if (_steps == ESteps.None)
				return;
			if (IsDone())
				return;

			// 创建下载器
			if (_steps == ESteps.PrepareDownload)
			{
				// 重置变量
				_downloadProgress = 0f;
				_downloadedBytes = 0;

				// 重置变量
				_isAbort = false;
				_latestDownloadBytes = 0;
				_latestDownloadRealtime = Time.realtimeSinceStartup;
				_tryAgainTimer = 0f;

				// 获取请求地址
				_requestURL = GetRequestURL();
				_steps = ESteps.CreateDownloader;
			}

			// 创建下载器
			if (_steps == ESteps.CreateDownloader)
			{
				_webRequest = DownloadSystem.NewRequest(_requestURL);

				if (CacheSystem.DisableUnityCacheOnWebGL)
				{
					uint crc = _bundleInfo.Bundle.UnityCRC;
					_downloadhandler = new DownloadHandlerAssetBundle(_requestURL, crc);
					_downloadhandler.autoLoadAssetBundle = false;
				}
				else
				{
					uint crc = _bundleInfo.Bundle.UnityCRC;
					var hash = Hash128.Parse(_bundleInfo.Bundle.FileHash);
					_downloadhandler = new DownloadHandlerAssetBundle(_requestURL, hash, crc);
					_downloadhandler.autoLoadAssetBundle = false;
				}

				_webRequest.downloadHandler = _downloadhandler;
				_webRequest.disposeDownloadHandlerOnDispose = false;
				_webRequest.SendWebRequest();
				_steps = ESteps.CheckDownload;
			}

			// 检测下载结果
			if (_steps == ESteps.CheckDownload)
			{
				_downloadProgress = _webRequest.downloadProgress;
				_downloadedBytes = _webRequest.downloadedBytes;
				if (_webRequest.isDone == false)
				{
					CheckTimeout();
					return;
				}

				bool hasError = false;

				// 检查网络错误
#if UNITY_2020_3_OR_NEWER
				if (_webRequest.result != UnityWebRequest.Result.Success)
				{
					hasError = true;
					_lastError = _webRequest.error;
					_lastCode = _webRequest.responseCode;
				}
#else
				if (_webRequest.isNetworkError || _webRequest.isHttpError)
				{
					hasError = true;
					_lastError = _webRequest.error;
					_lastCode = _webRequest.responseCode;
				}
#endif

				// 如果网络异常
				if (hasError)
				{
					_steps = ESteps.TryAgain;
				}
				else
				{
					_status = EStatus.Succeed;
					_steps = ESteps.Done;
					_lastError = string.Empty;
					_lastCode = 0;
				}

				// 最终释放请求
				DisposeRequest();

				if (_keepDownloadHandleLife == false)
					DisposeHandler();
			}

			// 重新尝试下载
			if (_steps == ESteps.TryAgain)
			{
				if (_failedTryAgain <= 0)
				{
					DisposeRequest();
					DisposeHandler();
					ReportError();
					_status = EStatus.Failed;
					_steps = ESteps.Done;
					return;
				}

				_tryAgainTimer += Time.unscaledDeltaTime;
				if (_tryAgainTimer > 1f)
				{
					_failedTryAgain--;
					_steps = ESteps.PrepareDownload;
					ReportWarning();
					YooLogger.Warning($"Try again download : {_requestURL}");
				}
			}
		}
		public override void Abort()
		{
			if (IsDone() == false)
			{
				_status = EStatus.Failed;
				_steps = ESteps.Done;
				_lastError = "user abort";
				_lastCode = 0;
	
				DisposeRequest();
				DisposeHandler();
			}
		}
		private void DisposeRequest()
		{
			if (_webRequest != null)
			{
				_webRequest.Dispose();
				_webRequest = null;
			}
		}

		/// <summary>
		/// 获取资源包
		/// </summary>
		public AssetBundle GetAssetBundle()
		{
			if (_downloadhandler != null)
				return _downloadhandler.assetBundle;

			return null;
		}

		/// <summary>
		/// 释放下载句柄
		/// </summary>
		public void DisposeHandler()
		{
			if (_downloadhandler != null)
			{
				_downloadhandler.Dispose();
				_downloadhandler = null;
			}
		}
	}
}