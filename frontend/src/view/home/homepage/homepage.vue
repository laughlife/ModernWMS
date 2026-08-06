<template>
  <div class="container">
    <div class="textBox">
      <div class="mainTitle">南阳有座山物流管理系统</div>
      <div class="subTitle">{{ i18n.global.t('homepage.subTitle') }}</div>
    </div>
    <div class="content">
      <img class="warehouseImage" src="@/assets/img/login-warehouse.png" alt="仓储物流管理" />
    </div>
  </div>
</template>

<script lang="ts" setup>
import i18n from '@/languages/i18n'
</script>

<style scoped lang="less">
.container {
  width: 100%;
  height: 100%;
  display: flex;
  justify-content: center;
  position: relative;
  align-items: center;
}
.VWMS{
  position: absolute;
  top: 0;
  display: flex;
  align-items: center;
  width: 40%;
  z-index: 999999999;
}

.content {
  width: min(72%, 1100px);
  padding-top: 110px;
}

.textBox {
  width: 100%;
  position: absolute;
  left: 50%;
  top: 5%;
  transform: translate(-50%, 0);
  letter-spacing: 5px;
  z-index: 999;
  font-family: 'Gill Sans', 'Gill Sans MT', Calibri, 'Trebuchet MS', sans-serif;
}

.mainTitle {
  display: flex;
  justify-content: center;
  width: 100%;
  color: #1769e8;
  font-weight: bold;
  font-size: clamp(44px, 5vw, 84px);
  line-height: 1.25;
  text-align: center;
}

.subTitle {
  display: flex;
  justify-content: center;
  width: 100%;
  font-size: 30px;
  letter-spacing: 5px;
  color: #76a9f2;
}

.warehouseImage {
  display: block;
  width: 100%;
  height: auto;
  object-fit: contain;
}

@media (max-width: 1280px) {
  .mainTitle {
    font-size: clamp(36px, 5vw, 64px);
  }

  .subTitle {
    font-size: 24px;
  }
}
</style>
