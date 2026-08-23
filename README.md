# Project-Virtual

Unity에서 iFacialMocap 앱과 연결하여 버추얼 캐릭터 조작을 연구하는 프로젝트입니다.

## 로컬 도구 설치

MMD 모델 변환에는 `MMD4Mecanim Beta 2020/01/05`를 사용합니다.

1. [Stereoarts 공식 배포 페이지](https://stereoarts.jp/)에서 `MMD4Mecanim_Beta_20200105.zip`을 내려받습니다.
2. 압축 파일의 `MMD4Mecanim.unitypackage`를 Unity 프로젝트에 임포트합니다.
3. 배포자의 재배포 금지 조건에 따라 `Assets/MMD4Mecanim`은 Git에 포함하지 않습니다.

MMD 모델과 모션을 사용하기 전에 각 배포자의 이용 규약을 반드시 확인해야 합니다.

## Git에 포함하지 않는 로컬 에셋

- `Assets/MMD4Mecanim`: 배포자가 공개 저장소 재배포를 금지한 변환 도구
- `Assets/Model/Tomarudo`: 모델 원본, 변환 모델, 텍스처와 애니메이션
- `Assets/YYB Hatsune Miku_default_1.0ver*`: YYB 미쿠 PMX와 MMD4Mecanim 변환 결과

YYB 미쿠는 원본 배포본의 Readme와 이용 규약을 로컬에서 보관하고 준수해야 합니다. 현재 저장소에는 원본 이용 규약이 포함되어 있지 않으므로 모델과 변환 결과를 재배포하지 않습니다.
