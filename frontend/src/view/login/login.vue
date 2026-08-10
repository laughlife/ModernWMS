<template>
  <div class="login-page">
    <header class="brand-bar">
      <div class="brand-lockup">
        <div class="brand-logo">
          <Logo :height="58" :top="0" :left="0" />
        </div>
        <div class="brand-copy">
          <strong>南阳有座山</strong>
          <span>物流管理系统</span>
        </div>
      </div>
    </header>

    <main class="login-main">
      <section class="hero-panel" aria-labelledby="system-title">
        <div class="hero-heading">
          <h1 id="system-title">南阳有座山物流管理系统</h1>
          <p>高效 · 智能 · 协同 · 安全</p>
        </div>

        <img
          class="hero-image"
          data-testid="login-hero-image"
          src="@/assets/img/login-warehouse.png"
          alt="仓储物流管理场景"
        />

        <div class="capability-strip" aria-label="系统核心能力">
          <article v-for="item in capabilities" :key="item.title" class="capability-item">
            <div class="capability-icon" aria-hidden="true">
              <v-icon :icon="item.icon" size="27" />
            </div>
            <div>
              <h2>{{ item.title }}</h2>
              <p>{{ item.description }}</p>
              <span>{{ item.detail }}</span>
            </div>
          </article>
        </div>
      </section>

      <section class="login-panel" aria-label="账号登录">
        <LoginForm />
      </section>
    </main>

    <footer class="login-footer">
      <div class="login-footer-support">技术支持：南阳锐翼网络科技有限责任公司</div>
      <div class="login-footer-registration">
        <a href="https://beian.miit.gov.cn/" target="_blank" rel="noopener noreferrer">
          <v-icon icon="mdi-shield-check" size="18" />
          <span>豫ICP备2025141776号-1</span>
        </a>
        <span class="login-footer-divider" aria-hidden="true">|</span>
        <a
          href="https://beian.mps.gov.cn/#/query/webSearch?code=41130202000523"
          target="_blank"
          rel="noopener noreferrer"
        >
          <v-icon icon="mdi-shield-check" size="18" />
          <span>豫公网安备41130202000523号</span>
        </a>
      </div>
    </footer>
  </div>
</template>

<script lang="ts" setup>
import { onMounted } from 'vue'
import LoginForm from '@/components/login/login-form.vue'
import Logo from '@/components/system/logo.vue'
import { emitter } from '@/utils/bus'

const capabilities = [
  {
    title: '库存管理',
    description: '实时库存监控',
    detail: '精准高效',
    icon: 'mdi-package-variant-closed'
  },
  {
    title: '运输管理',
    description: '运输全程跟踪',
    detail: '安全可控',
    icon: 'mdi-truck-outline'
  },
  {
    title: '订单管理',
    description: '订单智能处理',
    detail: '高效协同',
    icon: 'mdi-clipboard-text-outline'
  },
  {
    title: '数据统计',
    description: '多维数据分析',
    detail: '决策支持',
    icon: 'mdi-chart-pie'
  }
]

// Return to the login interface to clear the status
onMounted(() => {
  emitter.emit('closeLoading')
})
</script>

<style scoped lang="less">
.login-page {
  --brand-blue: #1769e8;
  --brand-deep: #172a4a;
  --muted-text: #77829a;

  min-height: 100vh;
  overflow-x: hidden;
  color: var(--brand-deep);
  background:
    radial-gradient(circle at 35% 32%, rgba(255, 255, 255, 0.96) 0, rgba(255, 255, 255, 0) 34%),
    linear-gradient(135deg, #f9fbff 0%, #edf4ff 56%, #f6f9ff 100%);
}

.brand-bar {
  height: 96px;
  padding: 0 clamp(32px, 7vw, 112px);
  display: flex;
  align-items: center;
  background: rgba(255, 255, 255, 0.96);
  box-shadow: 0 3px 14px rgba(40, 78, 132, 0.09);
}

.brand-lockup {
  display: flex;
  align-items: center;
  gap: 12px;
}

.brand-logo {
  position: relative;
  width: 62px;
  height: 58px;
  flex: 0 0 auto;

  :deep(.SysTitleLogo) {
    width: 58px;
    object-fit: contain;
  }
}

.brand-copy {
  display: flex;
  flex-direction: column;
  line-height: 1.15;

  strong {
    color: #1558b8;
    font-size: 25px;
    letter-spacing: 1px;
  }

  span {
    margin-top: 5px;
    color: #71819b;
    font-size: 13px;
    letter-spacing: 8px;
  }
}

.login-main {
  width: min(1500px, calc(100% - 64px));
  min-height: calc(100vh - 184px);
  margin: 0 auto;
  padding: clamp(34px, 5vh, 58px) 0 28px;
  display: grid;
  grid-template-columns: minmax(0, 1.75fr) minmax(390px, 0.9fr);
  gap: clamp(38px, 6vw, 92px);
  align-items: center;
  box-sizing: border-box;
}

.hero-panel {
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.hero-heading {
  text-align: center;

  h1 {
    margin: 0;
    color: #17243d;
    font-size: clamp(31px, 3vw, 48px);
    font-weight: 700;
    letter-spacing: 1px;
    line-height: 1.25;
  }

  p {
    margin: 13px 0 0;
    color: #2875eb;
    font-size: clamp(17px, 1.45vw, 23px);
    letter-spacing: 4px;
  }
}

.hero-image {
  width: min(100%, 820px);
  max-height: 430px;
  margin: 18px 0 20px;
  object-fit: contain;
  mix-blend-mode: multiply;
}

.capability-strip {
  width: 100%;
  min-height: 122px;
  padding: 22px 24px;
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
  box-sizing: border-box;
  background: rgba(255, 255, 255, 0.96);
  border: 1px solid rgba(225, 234, 247, 0.9);
  border-radius: 14px;
  box-shadow: 0 12px 30px rgba(49, 83, 132, 0.11);
}

.capability-item {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 13px;

  h2,
  p,
  span {
    margin: 0;
  }

  h2 {
    margin-bottom: 7px;
    color: #1262d7;
    font-size: 16px;
    font-weight: 700;
  }

  p,
  span {
    display: block;
    color: #7b879a;
    font-size: 12px;
    line-height: 1.7;
    white-space: nowrap;
  }
}

.capability-icon {
  width: 52px;
  height: 52px;
  flex: 0 0 52px;
  display: grid;
  place-items: center;
  color: #4387ef;
  background: #edf4ff;
  border-radius: 50%;
}

.login-panel {
  width: 100%;
  min-height: 610px;
  display: flex;
  align-items: center;
  background: rgba(255, 255, 255, 0.98);
  border: 1px solid rgba(223, 231, 243, 0.9);
  border-radius: 14px;
  box-shadow: 0 18px 48px rgba(54, 83, 127, 0.13);
}

.login-footer {
  min-height: 88px;
  padding: 14px 20px 18px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  box-sizing: border-box;
  color: #7f899c;
  background: rgba(237, 244, 255, 0.72);
  font-size: 13px;

  a {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    color: inherit;
    text-decoration: none;

    &:hover,
    &:focus-visible {
      color: #2875e9;
    }
  }
}

.login-footer-support {
  font-size: 14px;
  font-weight: 600;
}

.login-footer-registration {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 18px;
  color: #929bad;
}

.login-footer-divider {
  color: #b5bdca;
}

@media (max-width: 1180px) {
  .login-main {
    grid-template-columns: minmax(0, 1.35fr) minmax(360px, 0.85fr);
    gap: 34px;
  }

  .capability-strip {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .hero-image {
    max-height: 360px;
  }
}

@media (max-width: 820px) {
  .brand-bar {
    height: 78px;
    padding: 0 24px;
  }

  .brand-logo {
    width: 48px;
    height: 46px;

    :deep(.SysTitleLogo) {
      width: 46px;
      height: 46px !important;
    }
  }

  .brand-copy {
    strong {
      font-size: 21px;
    }

    span {
      font-size: 11px;
      letter-spacing: 6px;
    }
  }

  .login-main {
    width: min(100% - 32px, 520px);
    min-height: calc(100vh - 156px);
    padding: 28px 0;
    display: flex;
  }

  .hero-panel {
    display: none;
  }

  .login-panel {
    min-height: 540px;
  }

  .login-footer {
    min-height: 78px;
  }
}

@media (max-width: 480px) {
  .brand-bar {
    height: 70px;
    padding: 0 18px;
  }

  .brand-copy {
    strong {
      font-size: 19px;
    }

    span {
      letter-spacing: 5px;
    }
  }

  .login-main {
    width: calc(100% - 24px);
    min-height: calc(100vh - 140px);
    padding: 18px 0;
  }

  .login-panel {
    min-height: 500px;
    border-radius: 12px;
  }

  .login-footer {
    min-height: 70px;
    font-size: 11px;
  }

  .login-footer-registration {
    flex-wrap: wrap;
    gap: 6px 12px;
  }
}
</style>
